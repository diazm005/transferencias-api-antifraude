using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Persistence;

/// <summary>
/// Mapea el modelo SQL existente del equipo 2. No crea tablas ni aplica migraciones al iniciar.
/// Los modelos Row pertenecen a infraestructura para mantener EF fuera de Domain y Application.
/// </summary>
public sealed class TransferenciasDbContext(DbContextOptions<TransferenciasDbContext> options)
    : DbContext(options)
{
    private const string FechaUtcSql = "TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')";

    public DbSet<BancoRow> Bancos => Set<BancoRow>();
    public DbSet<SegmentoRow> Segmentos => Set<SegmentoRow>();
    public DbSet<ClienteRow> Clientes => Set<ClienteRow>();
    public DbSet<CuentaRow> Cuentas => Set<CuentaRow>();
    public DbSet<EstadoTransferenciaRow> EstadosTransferencia => Set<EstadoTransferenciaRow>();
    public DbSet<NivelRiesgoRow> NivelesRiesgo => Set<NivelRiesgoRow>();
    public DbSet<TransferenciaRow> Transferencias => Set<TransferenciaRow>();
    public DbSet<AuditoriaTransferenciaRow> AuditoriasTransferencia => Set<AuditoriaTransferenciaRow>();
    public DbSet<MovimientoCuentaRow> MovimientosCuenta => Set<MovimientoCuentaRow>();
    public DbSet<EvaluacionFraudeRow> EvaluacionesFraude => Set<EvaluacionFraudeRow>();
    public DbSet<NotificacionClienteRow> NotificacionesCliente => Set<NotificacionClienteRow>();

    /// <summary>Conserva exclusivamente los índices definidos en el script SQL revisado por el equipo.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    /// <summary>Define tipos, claves, relaciones, índices y columnas generadas de las once tablas.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("transferencias");
        ConfigurarCatalogos(modelBuilder);
        ConfigurarClientesYCuentas(modelBuilder);
        ConfigurarTransferencias(modelBuilder);
        ConfigurarAuditoriaYMovimientos(modelBuilder);
        ConfigurarFraudeYNotificaciones(modelBuilder);
    }

    /// <summary>Mapea catálogos; los identificadores de estado y riesgo respetan los enums existentes.</summary>
    private static void ConfigurarCatalogos(ModelBuilder modelBuilder)
    {
        var banco = modelBuilder.Entity<BancoRow>();
        banco.ToTable("Banco", table =>
        {
            table.HasCheckConstraint("CK_Banco_Codigo", "LEN(LTRIM(RTRIM(Codigo))) > 0");
            table.HasCheckConstraint("CK_Banco_Nombre", "LEN(LTRIM(RTRIM(Nombre))) > 0");
        });
        banco.HasKey(x => x.BancoId).HasName("PK_Banco");
        banco.Property(x => x.BancoId).UseIdentityColumn();
        banco.Property(x => x.Codigo).HasColumnType("varchar(20)").IsRequired();
        banco.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        banco.Property(x => x.EsBancoPropio).HasDefaultValue(false);
        banco.Property(x => x.Activo).HasDefaultValue(true);
        banco.HasAlternateKey(x => x.Codigo).HasName("UQ_Banco_Codigo");
        banco.HasIndex(x => x.EsBancoPropio).IsUnique().HasFilter("EsBancoPropio = 1")
            .HasDatabaseName("UX_Banco_Propio");

        var segmento = modelBuilder.Entity<SegmentoRow>();
        segmento.ToTable("Segmento", table =>
        {
            table.HasCheckConstraint("CK_Segmento_Limite", "LimiteDiario >= 0");
            table.HasCheckConstraint("CK_Segmento_Nombre", "LEN(LTRIM(RTRIM(Nombre))) > 0");
        });
        segmento.HasKey(x => x.SegmentoId).HasName("PK_Segmento");
        segmento.Property(x => x.SegmentoId).UseIdentityColumn();
        segmento.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
        segmento.Property(x => x.LimiteDiario).HasPrecision(18, 2);
        segmento.Property(x => x.Activo).HasDefaultValue(true);
        segmento.HasAlternateKey(x => x.Nombre).HasName("UQ_Segmento_Nombre");

        var estado = modelBuilder.Entity<EstadoTransferenciaRow>();
        estado.ToTable("EstadoTransferencia");
        estado.HasKey(x => x.EstadoTransferenciaId).HasName("PK_EstadoTransferencia");
        estado.Property(x => x.EstadoTransferenciaId).ValueGeneratedNever();
        estado.Property(x => x.Nombre).HasColumnType("varchar(30)").IsRequired();
        estado.Property(x => x.Descripcion).HasMaxLength(250).IsRequired();
        estado.HasAlternateKey(x => x.Nombre).HasName("UQ_EstadoTransferencia_Nombre");

        var riesgo = modelBuilder.Entity<NivelRiesgoRow>();
        riesgo.ToTable("NivelRiesgo");
        riesgo.HasKey(x => x.NivelRiesgoId).HasName("PK_NivelRiesgo");
        riesgo.Property(x => x.NivelRiesgoId).ValueGeneratedNever();
        riesgo.Property(x => x.Nombre).HasColumnType("varchar(20)").IsRequired();
        riesgo.Property(x => x.Descripcion).HasMaxLength(250).IsRequired();
        riesgo.HasAlternateKey(x => x.Nombre).HasName("UQ_NivelRiesgo_Nombre");
    }

    /// <summary>Mapea titulares y cuentas, incluidas las claves compuestas que validan el origen y destino.</summary>
    private static void ConfigurarClientesYCuentas(ModelBuilder modelBuilder)
    {
        var cliente = modelBuilder.Entity<ClienteRow>();
        cliente.ToTable("Cliente", table =>
        {
            table.HasCheckConstraint("CK_Cliente_Dni", "LEN(Dni) = 8 AND Dni COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'");
            table.HasCheckConstraint("CK_Cliente_Nombres", "LEN(LTRIM(RTRIM(Nombres))) > 0");
        });
        cliente.HasKey(x => x.ClienteId).HasName("PK_Cliente");
        cliente.Property(x => x.ClienteId).UseIdentityColumn();
        cliente.Property(x => x.Dni).HasColumnType("varchar(8)").IsRequired();
        cliente.Property(x => x.Nombres).HasMaxLength(150).IsRequired();
        cliente.Property(x => x.Activo).HasDefaultValue(true);
        cliente.Property(x => x.FechaCreacion).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        cliente.HasAlternateKey(x => x.Dni).HasName("UQ_Cliente_Dni");
        cliente.HasIndex(x => x.SegmentoId).HasDatabaseName("IX_Cliente_Segmento");
        cliente.HasOne<SegmentoRow>().WithMany().HasForeignKey(x => x.SegmentoId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Cliente_Segmento");

        var cuenta = modelBuilder.Entity<CuentaRow>();
        cuenta.ToTable("Cuenta", table =>
        {
            table.HasCheckConstraint("CK_Cuenta_Numero", "LEN(LTRIM(RTRIM(NumeroCuenta))) > 0");
            table.HasCheckConstraint("CK_Cuenta_Saldo", "SaldoDisponible >= 0");
            table.HasCheckConstraint("CK_Cuenta_Moneda", "Moneda = 'PEN'");
        });
        cuenta.HasKey(x => x.CuentaId).HasName("PK_Cuenta");
        cuenta.Property(x => x.CuentaId).UseIdentityColumn();
        cuenta.Property(x => x.NumeroCuenta).HasColumnType("varchar(30)").IsRequired();
        cuenta.Property(x => x.Activa).HasDefaultValue(true);
        cuenta.Property(x => x.Moneda).HasColumnType("char(3)").HasDefaultValue("PEN").IsRequired();
        cuenta.Property(x => x.SaldoDisponible).HasPrecision(18, 2).HasDefaultValue(0m);
        cuenta.Property(x => x.VersionFila).IsRowVersion().IsRequired();
        cuenta.Property(x => x.FechaCreacion).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        cuenta.HasAlternateKey(x => new { x.BancoId, x.NumeroCuenta }).HasName("UQ_Cuenta_BancoNumero");
        cuenta.HasAlternateKey(x => new { x.CuentaId, x.ClienteId }).HasName("UQ_Cuenta_IdCliente");
        cuenta.HasAlternateKey(x => new { x.CuentaId, x.BancoId, x.NumeroCuenta }).HasName("UQ_Cuenta_IdBancoNumero");
        cuenta.HasIndex(x => x.ClienteId).HasDatabaseName("IX_Cuenta_Cliente");
        cuenta.HasOne<ClienteRow>().WithMany().HasForeignKey(x => x.ClienteId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Cuenta_Cliente");
        cuenta.HasOne<BancoRow>().WithMany().HasForeignKey(x => x.BancoId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Cuenta_Banco");
    }

    /// <summary>Mapea la operación y deja que SQL calcule el total y el día de negocio.</summary>
    private static void ConfigurarTransferencias(ModelBuilder modelBuilder)
    {
        var transferencia = modelBuilder.Entity<TransferenciaRow>();
        transferencia.ToTable("Transferencia", table =>
        {
            table.HasCheckConstraint("CK_Transferencia_Id", "TransferenciaId <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Transferencia_Monto", "Monto > 0");
            table.HasCheckConstraint("CK_Transferencia_Comision", "Comision >= 0");
            table.HasCheckConstraint("CK_Transferencia_Moneda", "Moneda = 'PEN'");
            table.HasCheckConstraint("CK_Transferencia_Destino", "LEN(LTRIM(RTRIM(NumeroCuentaDestino))) > 0");
            table.HasCheckConstraint("CK_Transferencia_CuentasDiferentes", "CuentaDestinoInternaId IS NULL OR CuentaDestinoInternaId <> CuentaOrigenId");
        });
        transferencia.HasKey(x => x.TransferenciaId).HasName("PK_Transferencia");
        transferencia.Property(x => x.TransferenciaId).ValueGeneratedNever();
        transferencia.Property(x => x.NumeroCuentaDestino).HasColumnType("varchar(30)").IsRequired();
        transferencia.Property(x => x.Monto).HasPrecision(18, 2);
        transferencia.Property(x => x.Comision).HasPrecision(18, 2).HasDefaultValue(0m);
        transferencia.Property(x => x.TotalDebito).HasPrecision(19, 2)
            .HasComputedColumnSql("Monto + Comision", stored: true);
        transferencia.Property(x => x.Moneda).HasColumnType("char(3)").HasDefaultValue("PEN").IsRequired();
        transferencia.Property(x => x.FechaSolicitud).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        transferencia.Property(x => x.FechaNegocio).HasColumnType("date")
            .HasComputedColumnSql("CONVERT(date, SWITCHOFFSET(FechaSolicitud, '-05:00'))", stored: true);
        transferencia.Property(x => x.Mensaje).HasMaxLength(500).HasDefaultValue(string.Empty).IsRequired();
        transferencia.Property(x => x.Notificada).HasDefaultValue(false);
        transferencia.Property(x => x.VersionFila).IsRowVersion().IsRequired();
        transferencia.HasIndex(x => new { x.ClienteId, x.FechaNegocio })
            .IncludeProperties(x => new { x.Monto, x.EstadoTransferenciaId, x.CuentaOrigenId })
            .HasDatabaseName("IX_Transferencia_ClienteDia");
        transferencia.HasIndex(x => new { x.CuentaOrigenId, x.FechaSolicitud })
            .HasDatabaseName("IX_Transferencia_OrigenFecha");
        transferencia.HasIndex(x => new { x.EstadoTransferenciaId, x.FechaSolicitud })
            .HasFilter("EstadoTransferenciaId = 3").HasDatabaseName("IX_Transferencia_Pendientes");
        transferencia.HasOne<ClienteRow>().WithMany().HasForeignKey(x => x.ClienteId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Transferencia_Cliente");
        transferencia.HasOne<CuentaRow>().WithMany().HasForeignKey(x => new { x.CuentaOrigenId, x.ClienteId })
            .HasPrincipalKey(x => new { x.CuentaId, x.ClienteId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Transferencia_OrigenTitular");
        transferencia.HasOne<BancoRow>().WithMany().HasForeignKey(x => x.BancoDestinoId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Transferencia_BancoDestino");
        transferencia.HasOne<CuentaRow>().WithMany()
            .HasForeignKey(x => new { x.CuentaDestinoInternaId, x.BancoDestinoId, x.NumeroCuentaDestino })
            .HasPrincipalKey(x => new { x.CuentaId, x.BancoId, x.NumeroCuenta })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Transferencia_DestinoInterno");
        transferencia.HasOne<EstadoTransferenciaRow>().WithMany().HasForeignKey(x => x.EstadoTransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Transferencia_Estado");
    }

    /// <summary>Mapea el historial y los efectos contables con unicidad para evitar movimientos duplicados.</summary>
    private static void ConfigurarAuditoriaYMovimientos(ModelBuilder modelBuilder)
    {
        var auditoria = modelBuilder.Entity<AuditoriaTransferenciaRow>();
        auditoria.ToTable("AuditoriaTransferencia");
        auditoria.HasKey(x => x.AuditoriaTransferenciaId).HasName("PK_AuditoriaTransferencia");
        auditoria.Property(x => x.AuditoriaTransferenciaId).UseIdentityColumn();
        auditoria.Property(x => x.FechaEvento).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        auditoria.Property(x => x.Mensaje).HasMaxLength(500).IsRequired();
        auditoria.Property(x => x.TraceIdentifier).HasMaxLength(100);
        auditoria.Property(x => x.Origen).HasMaxLength(100).HasDefaultValue("Application").IsRequired();
        auditoria.HasAlternateKey(x => new { x.AuditoriaTransferenciaId, x.TransferenciaId })
            .HasName("UQ_Auditoria_IdOperacion");
        auditoria.HasIndex(x => new { x.TransferenciaId, x.FechaEvento, x.AuditoriaTransferenciaId })
            .HasDatabaseName("IX_Auditoria_TransferenciaFecha");
        auditoria.HasOne<TransferenciaRow>().WithMany().HasForeignKey(x => x.TransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Auditoria_Transferencia");
        auditoria.HasOne<EstadoTransferenciaRow>().WithMany().HasForeignKey(x => x.EstadoTransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Auditoria_Estado");

        var movimiento = modelBuilder.Entity<MovimientoCuentaRow>();
        movimiento.ToTable("MovimientoCuenta", table =>
        {
            table.HasCheckConstraint("CK_Movimiento_Monto", "Monto > 0");
            table.HasCheckConstraint("CK_Movimiento_Saldos", "SaldoAnterior >= 0 AND SaldoPosterior >= 0");
            table.HasCheckConstraint("CK_Movimiento_Tipo", "(TipoMovimiento IN ('DEBITO_MONTO','DEBITO_COMISION','REVERSO_DESTINO') AND Naturaleza = 'D') OR (TipoMovimiento IN ('CREDITO_DESTINO','REVERSO_MONTO','REVERSO_COMISION') AND Naturaleza = 'C')");
            table.HasCheckConstraint("CK_Movimiento_Calculo", "SaldoPosterior = CASE WHEN Naturaleza = 'D' THEN SaldoAnterior - Monto ELSE SaldoAnterior + Monto END");
        });
        movimiento.HasKey(x => x.MovimientoCuentaId).HasName("PK_MovimientoCuenta");
        movimiento.Property(x => x.MovimientoCuentaId).UseIdentityColumn();
        movimiento.Property(x => x.TipoMovimiento).HasColumnType("varchar(20)").IsRequired();
        movimiento.Property(x => x.Naturaleza).HasColumnType("char(1)").IsRequired();
        movimiento.Property(x => x.Monto).HasPrecision(18, 2);
        movimiento.Property(x => x.SaldoAnterior).HasPrecision(18, 2);
        movimiento.Property(x => x.SaldoPosterior).HasPrecision(18, 2);
        movimiento.Property(x => x.FechaMovimiento).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        movimiento.HasAlternateKey(x => new { x.TransferenciaId, x.CuentaId, x.TipoMovimiento })
            .HasName("UQ_Movimiento_OperacionCuentaTipo");
        movimiento.HasIndex(x => new { x.CuentaId, x.FechaMovimiento }).HasDatabaseName("IX_Movimiento_CuentaFecha");
        movimiento.HasOne<TransferenciaRow>().WithMany().HasForeignKey(x => x.TransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Movimiento_Transferencia");
        movimiento.HasOne<CuentaRow>().WithMany().HasForeignKey(x => x.CuentaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Movimiento_Cuenta");
    }

    /// <summary>Mapea las colas persistentes de evaluación de fraude y entrega de notificaciones.</summary>
    private static void ConfigurarFraudeYNotificaciones(ModelBuilder modelBuilder)
    {
        var fraude = modelBuilder.Entity<EvaluacionFraudeRow>();
        fraude.ToTable("EvaluacionFraude", table =>
        {
            table.HasCheckConstraint("CK_Fraude_Intento", "NumeroIntento > 0");
            table.HasCheckConstraint("CK_Fraude_Estado", "EstadoEvaluacion IN ('Pendiente','Procesando','Completada','Error')");
            table.HasCheckConstraint("CK_Fraude_Resultado", "(EstadoEvaluacion IN ('Pendiente','Procesando') AND NivelRiesgoId IS NULL AND FechaRespuesta IS NULL) OR (EstadoEvaluacion = 'Completada' AND NivelRiesgoId IS NOT NULL AND FechaRespuesta IS NOT NULL) OR (EstadoEvaluacion = 'Error' AND NivelRiesgoId IS NULL AND FechaRespuesta IS NOT NULL)");
            table.HasCheckConstraint("CK_Fraude_Fechas", "FechaRespuesta IS NULL OR FechaRespuesta >= FechaSolicitud");
        });
        fraude.HasKey(x => x.EvaluacionFraudeId).HasName("PK_EvaluacionFraude");
        fraude.Property(x => x.EvaluacionFraudeId).UseIdentityColumn();
        fraude.Property(x => x.NumeroIntento).HasDefaultValue(1);
        fraude.Property(x => x.EstadoEvaluacion).HasColumnType("varchar(12)").HasDefaultValue("Pendiente").IsRequired();
        fraude.Property(x => x.Motivo).HasMaxLength(500);
        fraude.Property(x => x.VersionRegla).HasColumnType("varchar(50)").IsRequired();
        fraude.Property(x => x.Proveedor).HasMaxLength(100).IsRequired();
        fraude.Property(x => x.FechaSolicitud).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        fraude.Property(x => x.FechaRespuesta).HasPrecision(3);
        fraude.Property(x => x.VersionFila).IsRowVersion().IsRequired();
        fraude.HasAlternateKey(x => new { x.TransferenciaId, x.NumeroIntento }).HasName("UQ_Fraude_OperacionIntento");
        fraude.HasIndex(x => new { x.EstadoEvaluacion, x.FechaSolicitud }).IncludeProperties(x => x.TransferenciaId)
            .HasDatabaseName("IX_Fraude_Pendientes");
        fraude.HasOne<TransferenciaRow>().WithMany().HasForeignKey(x => x.TransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Fraude_Transferencia");
        fraude.HasOne<NivelRiesgoRow>().WithMany().HasForeignKey(x => x.NivelRiesgoId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Fraude_Nivel");

        var notificacion = modelBuilder.Entity<NotificacionClienteRow>();
        notificacion.ToTable("NotificacionCliente", table =>
        {
            table.HasCheckConstraint("CK_Notificacion_Canal", "Canal IN ('SMS','Correo')");
            table.HasCheckConstraint("CK_Notificacion_Estado", "EstadoEnvio IN ('Pendiente','Enviada','Error')");
            table.HasCheckConstraint("CK_Notificacion_Intentos", "Intentos >= 0");
            table.HasCheckConstraint("CK_Notificacion_Fecha", "(EstadoEnvio = 'Enviada' AND FechaEnvio IS NOT NULL AND Intentos > 0) OR (EstadoEnvio IN ('Pendiente','Error') AND FechaEnvio IS NULL)");
            table.HasCheckConstraint("CK_Notificacion_FechaOrden", "FechaEnvio IS NULL OR FechaEnvio >= FechaCreacion");
        });
        notificacion.HasKey(x => x.NotificacionClienteId).HasName("PK_NotificacionCliente");
        notificacion.Property(x => x.NotificacionClienteId).UseIdentityColumn();
        notificacion.Property(x => x.Canal).HasColumnType("varchar(10)").IsRequired();
        notificacion.Property(x => x.EstadoEnvio).HasColumnType("varchar(10)").HasDefaultValue("Pendiente").IsRequired();
        notificacion.Property(x => x.Intentos).HasDefaultValue(0);
        notificacion.Property(x => x.FechaCreacion).HasPrecision(3).HasDefaultValueSql(FechaUtcSql);
        notificacion.Property(x => x.FechaEnvio).HasPrecision(3);
        notificacion.Property(x => x.ProximoIntento).HasPrecision(3);
        notificacion.Property(x => x.UltimoError).HasMaxLength(500);
        notificacion.Property(x => x.VersionFila).IsRowVersion().IsRequired();
        notificacion.HasAlternateKey(x => new { x.AuditoriaTransferenciaId, x.Canal }).HasName("UQ_Notificacion_EventoCanal");
        notificacion.HasIndex(x => new { x.EstadoEnvio, x.ProximoIntento })
            .IncludeProperties(x => new { x.TransferenciaId, x.Intentos }).HasDatabaseName("IX_Notificacion_Pendientes");
        notificacion.HasOne<TransferenciaRow>().WithMany().HasForeignKey(x => x.TransferenciaId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Notificacion_Transferencia");
        notificacion.HasOne<AuditoriaTransferenciaRow>().WithMany()
            .HasForeignKey(x => new { x.AuditoriaTransferenciaId, x.TransferenciaId })
            .HasPrincipalKey(x => new { x.AuditoriaTransferenciaId, x.TransferenciaId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Notificacion_Auditoria");
    }
}
