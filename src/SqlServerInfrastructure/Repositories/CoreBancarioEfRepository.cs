using System.Data;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Repositories;

/// <summary>
/// Core local del taller sobre SQL Server. Cada método utiliza su propio contexto y el débito
/// protege saldo, límite, movimientos y crédito interno dentro de una sola transacción.
/// </summary>
public sealed class CoreBancarioEfRepository(
    IDbContextFactory<TransferenciasDbContext> contextFactory, TimeProvider clock)
    : IConsultarClienteRepository, IConsultarCuentaRepository, IConsultarBancoRepository, ICoreBancarioGateway
{
    private static readonly TimeSpan DesfaseLima = TimeSpan.FromHours(-5);

    /// <summary>Busca un cliente activo y conserva el nombre del segmento esperado por el dominio.</summary>
    public async Task<Cliente?> ObtenerClienteAsync(string dni, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await (from cliente in db.Clientes.AsNoTracking()
                      join segmento in db.Segmentos.AsNoTracking() on cliente.SegmentoId equals segmento.SegmentoId
                      where cliente.Dni == dni && cliente.Activo
                      select new Cliente(cliente.Dni, cliente.Nombres, segmento.Nombre)).SingleOrDefaultAsync(ct);
    }

    /// <summary>Consulta únicamente cuentas del banco propio activo; devuelve también las cuentas inactivas para validarlas.</summary>
    public async Task<Cuenta?> ObtenerCuentaAsync(string numero, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await (from cuenta in db.Cuentas.AsNoTracking()
                      join cliente in db.Clientes.AsNoTracking() on cuenta.ClienteId equals cliente.ClienteId
                      join banco in db.Bancos.AsNoTracking() on cuenta.BancoId equals banco.BancoId
                      where cuenta.NumeroCuenta == numero && banco.EsBancoPropio && banco.Activo && cliente.Activo
                      select new Cuenta(cuenta.NumeroCuenta, cliente.Dni, banco.Codigo,
                          cuenta.Activa, cuenta.SaldoDisponible)).SingleOrDefaultAsync(ct);
    }

    /// <summary>Comprueba que el código receptor esté registrado y habilitado antes de iniciar la operación.</summary>
    public async Task<bool> ExisteAsync(string codigo, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Bancos.AsNoTracking().AnyAsync(x => x.Codigo == codigo && x.Activo, ct);
    }

    /// <summary>Calcula el consumo diario por débitos reales, descontando solo reversos de operaciones de ese mismo día.</summary>
    public async Task<decimal> ObtenerAcumuladoAsync(string clienteDni, DateOnly fecha, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var clienteId = await db.Clientes.Where(x => x.Dni == clienteDni)
            .Select(x => (long?)x.ClienteId).SingleOrDefaultAsync(ct);
        return clienteId is null ? 0m : await CalcularAcumuladoAsync(db, clienteId.Value, fecha, ct);
    }

    /// <summary>
    /// Revalida la operación con los valores vigentes bajo bloqueo y contabiliza de forma atómica.
    /// Fecha y límite recibidos mantienen el contrato; la decisión final usa el reloj y segmento actuales
    /// para impedir un límite obsoleto o un débito asignado al día anterior tras esperar un bloqueo.
    /// </summary>
    public async Task<string?> DebitarAsync(
        Transferencia transferencia, DateOnly fecha, decimal limite, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var snapshot = await db.Transferencias.SingleOrDefaultAsync(x => x.TransferenciaId == transferencia.Id, ct)
            ?? throw new InvalidOperationException("Debe persistir la solicitud antes de intentar mover dinero.");
        var original = await TransferenciaEfMapper.ObtenerAsync(db, transferencia.Id, ct)
            ?? throw new InvalidOperationException("La transferencia no tiene referencias válidas.");
        TransferenciaEfMapper.ExigirMismaSolicitud(original, transferencia);

        // El candado SQL se comparte entre instancias y serializa todos los débitos de un cliente,
        // incluso si utiliza cuentas distintas. Se libera al confirmar o revertir la transacción.
        await BloquearClienteAsync(db, snapshot.ClienteId, ct);
        var existentes = await db.MovimientosCuenta.AsNoTracking()
            .Where(x => x.TransferenciaId == transferencia.Id).ToListAsync(ct);
        var debitoPrevio = existentes.SingleOrDefault(x => x.TipoMovimiento == "DEBITO_MONTO");
        if (debitoPrevio is not null)
        {
            if (debitoPrevio.CuentaId != snapshot.CuentaOrigenId || debitoPrevio.Monto != snapshot.Monto)
                throw new InvalidOperationException("El débito registrado no coincide con la solicitud.");
            // Un resultado ya contabilizado se reutiliza: nunca se vuelve a descontar el dinero.
            return null;
        }
        if (existentes.Count != 0)
            throw new InvalidOperationException("La operación tiene movimientos incompletos que requieren conciliación.");
        if (snapshot.EstadoTransferenciaId != (byte)EstadoTransferencia.Pendiente)
            throw new InvalidOperationException("Solo una operación pendiente puede iniciar el movimiento financiero.");

        var bancoDestino = await db.Bancos.FromSqlInterpolated($"""
            SELECT * FROM [transferencias].[Banco] WITH (HOLDLOCK)
            WHERE [BancoId] = {snapshot.BancoDestinoId}
            """).SingleAsync(ct);
        if (!bancoDestino.Activo) return "El banco destino no está disponible.";
        var destinoId = bancoDestino.EsBancoPropio
            ? await db.Cuentas.Where(x => x.BancoId == snapshot.BancoDestinoId &&
                x.NumeroCuenta == snapshot.NumeroCuentaDestino).Select(x => (long?)x.CuentaId).SingleOrDefaultAsync(ct)
            : null;
        if (bancoDestino.EsBancoPropio && destinoId is null) return "La cuenta de destino no existe.";
        if (destinoId == snapshot.CuentaOrigenId) return "Las cuentas de origen y destino deben ser diferentes.";

        // Ordenar las cuentas evita que dos transferencias cruzadas las bloqueen en orden inverso.
        var ids = new[] { snapshot.CuentaOrigenId }.Concat(destinoId.HasValue ? [destinoId.Value] : [])
            .Distinct().Order().ToArray();
        var cuentas = new Dictionary<long, CuentaRow>();
        foreach (var id in ids)
        {
            var cuenta = await db.Cuentas.FromSqlInterpolated($"""
                SELECT * FROM [transferencias].[Cuenta] WITH (UPDLOCK, HOLDLOCK)
                WHERE [CuentaId] = {id}
                """).SingleOrDefaultAsync(ct);
            if (cuenta is null) return "Una de las cuentas de la operación ya no existe.";
            cuentas.Add(id, cuenta);
        }

        var origen = cuentas[snapshot.CuentaOrigenId];
        var cliente = await db.Clientes.FromSqlInterpolated($"""
            SELECT * FROM [transferencias].[Cliente] WITH (HOLDLOCK)
            WHERE [ClienteId] = {snapshot.ClienteId}
            """).SingleAsync(ct);
        var segmento = await db.Segmentos.FromSqlInterpolated($"""
            SELECT * FROM [transferencias].[Segmento] WITH (HOLDLOCK)
            WHERE [SegmentoId] = {cliente.SegmentoId}
            """).SingleAsync(ct);
        var bancoOrigen = await db.Bancos.FromSqlInterpolated($"""
            SELECT * FROM [transferencias].[Banco] WITH (HOLDLOCK)
            WHERE [BancoId] = {origen.BancoId}
            """).SingleAsync(ct);
        if (!cliente.Activo || origen.ClienteId != cliente.ClienteId)
            return "Cliente o cuenta de origen inválidos.";
        if (!bancoOrigen.EsBancoPropio || !bancoOrigen.Activo)
            return "La cuenta de origen no pertenece al banco propio activo.";
        if (!segmento.Activo) return "El segmento del cliente no está habilitado.";

        var instante = TransferenciaEfMapper.FechaSql(clock.GetUtcNow());
        var fechaEfectiva = DateOnly.FromDateTime(instante.ToOffset(DesfaseLima).DateTime);
        var acumulado = await CalcularAcumuladoAsync(db, cliente.ClienteId, fechaEfectiva, ct);
        var error = Reglas.Validar(new Cuenta(origen.NumeroCuenta, cliente.Dni, bancoOrigen.Codigo,
            origen.Activa, origen.SaldoDisponible), snapshot.Monto, snapshot.Comision, acumulado, segmento.LimiteDiario);
        if (error is not null) return error;

        CuentaRow? destino = destinoId.HasValue ? cuentas[destinoId.Value] : null;
        if (destino is not null)
        {
            if (!destino.Activa || destino.Moneda != "PEN") return "La cuenta de destino no está activa o su moneda es inválida.";
            if (!await db.Clientes.AnyAsync(x => x.ClienteId == destino.ClienteId && x.Activo, ct))
                return "El titular de la cuenta de destino no está activo.";
            snapshot.CuentaDestinoInternaId = destino.CuentaId;
        }

        RegistrarMovimiento(db, origen, snapshot, "DEBITO_MONTO", "D", snapshot.Monto, instante);
        if (snapshot.Comision > 0)
            RegistrarMovimiento(db, origen, snapshot, "DEBITO_COMISION", "D", snapshot.Comision, instante);
        if (destino is not null)
            RegistrarMovimiento(db, destino, snapshot, "CREDITO_DESTINO", "C", snapshot.Monto, instante);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    /// <summary>
    /// Agrupa por el día efectivo del débito, no por la fecha de solicitud. Un reverso de un débito
    /// de ayer no libera cupo de hoy; las comisiones y los créditos al destino tampoco consumen cupo.
    /// </summary>
    private static async Task<decimal> CalcularAcumuladoAsync(
        TransferenciasDbContext db, long clienteId, DateOnly fecha, CancellationToken ct)
    {
        var desde = new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue), DesfaseLima);
        var hasta = desde.AddDays(1);
        var importes =
            from debito in db.MovimientosCuenta.AsNoTracking()
            join transferencia in db.Transferencias.AsNoTracking() on debito.TransferenciaId equals transferencia.TransferenciaId
            join reverso in db.MovimientosCuenta.AsNoTracking().Where(x => x.TipoMovimiento == "REVERSO_MONTO" && x.FechaMovimiento < hasta)
                on new { debito.TransferenciaId, debito.CuentaId } equals new { reverso.TransferenciaId, reverso.CuentaId } into reversos
            from reverso in reversos.DefaultIfEmpty()
            where transferencia.ClienteId == clienteId && debito.TipoMovimiento == "DEBITO_MONTO" &&
                debito.FechaMovimiento >= desde && debito.FechaMovimiento < hasta
            select (decimal?)(debito.Monto - (reverso == null ? 0m : reverso.Monto));
        return Math.Max(0m, await importes.SumAsync(ct) ?? 0m);
    }

    /// <summary>Modifica el saldo y agrega su evidencia contable; SaveChanges confirma todos los conceptos juntos.</summary>
    private static void RegistrarMovimiento(TransferenciasDbContext db, CuentaRow cuenta,
        TransferenciaRow transferencia, string tipo, string naturaleza, decimal monto, DateTimeOffset fecha)
    {
        var anterior = cuenta.SaldoDisponible;
        cuenta.SaldoDisponible = naturaleza == "D" ? anterior - monto : anterior + monto;
        db.MovimientosCuenta.Add(new MovimientoCuentaRow
        {
            TransferenciaId = transferencia.TransferenciaId,
            CuentaId = cuenta.CuentaId,
            TipoMovimiento = tipo,
            Naturaleza = naturaleza,
            Monto = monto,
            SaldoAnterior = anterior,
            SaldoPosterior = cuenta.SaldoDisponible,
            FechaMovimiento = fecha
        });
    }

    /// <summary>Adquiere un candado lógico SQL con espera acotada, sin depender de un semáforo en memoria.</summary>
    private static async Task BloquearClienteAsync(TransferenciasDbContext db, long clienteId, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = 10;
        command.CommandText = """
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = @recurso,
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            SELECT @resultado;
            """;
        var recurso = command.CreateParameter();
        recurso.ParameterName = "@recurso";
        recurso.DbType = DbType.String;
        recurso.Size = 255;
        recurso.Value = $"CleanTransferencia:cliente:{clienteId}";
        command.Parameters.Add(recurso);
        var resultado = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (resultado < 0)
            throw new TimeoutException("No se pudo obtener el turno SQL del cliente para realizar el débito.");
    }
}
