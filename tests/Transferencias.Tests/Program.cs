using Application.Adapters;
using Application.Handlers.Transferencias;
using Application.Internals.Adapters;
using Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using AntifraudeInfrastructure;
using AuditoriaInfrastructure;
using CamaraCompensatorioInfrastructure;
using CoreBancarioInfrastructure;
using Domain.Entities.Internals;
using Domain.Interfaces;
using NotificacionInfrastructure;

var total = 0;
void Check(bool ok, string nombre)
{
    if (!ok) throw new Exception("FALLÓ: " + nombre);
    Console.WriteLine("OK: " + nombre);
    total++;
}

InitiatePaymentExecutionRequest Solicitud(decimal monto = 100, string destino = "EXTERNA") =>
    new(Guid.NewGuid(), "12345678", "001", destino, "BANCO_B", monto);

(CasoPrueba Caso, CoreBancarioAdapter Core, AuditoriaRepository Auditoria) Crear(
    INotificadorCliente? notificador = null, TimeProvider? reloj = null,
    IAntifraudeService? antifraude = null, CoreBancarioAdapter? coreCompartido = null,
    AuditoriaRepository? auditoriaCompartida = null, IControlOperacion? control = null,
    ICamaraCompensacionGateway? camara = null)
{
    var core = coreCompartido ?? new CoreBancarioAdapter();
    var auditoria = auditoriaCompartida ?? new AuditoriaRepository();
    var handler = new RealizarTransferenciaHandler(core, core, core, antifraude ?? new AntifraudeAdapter(), new SegmentoLimiteRepository(),
        new OtpSimulado(), camara ?? new CamaraCompensacionAdapter(), auditoria,
        notificador ?? new NotificacionesAdapter(), reloj ?? TimeProvider.System,
        NullLogger<RealizarTransferenciaHandler>.Instance, core, control ?? new ControlOperacionMemoria());
    return (new(handler), core, auditoria);
}

var f = Crear();
var s = Solicitud();
var r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Completed && r.Fee == 5 && r.CustomerNotified, "Interbancaria, comisión y notificación");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Débito incluye comisión");
await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Caso.EjecutarAsync(s)));
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Reintentos concurrentes no duplican débitos");
try { await f.Caso.EjecutarAsync(s with { Amount = 200 }); Check(false, "ID reutilizado"); }
catch (InvalidOperationException) { Check(true, "ID reutilizado con otros datos rechazado"); }

foreach (var monto in new[] { 0m, -1m, 1.001m })
{
    try { await f.Caso.EjecutarAsync(Solicitud(monto)); Check(false, "Monto inválido"); }
    catch (ArgumentException) { Check(true, "Monto inválido: " + monto); }
}

f = Crear();
s = Solicitud(10_000);
r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "OTP desde exactamente 10 000");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000, "Solicitar OTP no debita");
r = await f.Caso.EjecutarAsync(s with { OtpCode = "incorrecto" });
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "OTP incorrecto");
r = await f.Caso.EjecutarAsync(s with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Completed, "Confirmar con mismo ID y OTP válido");
Check(f.Auditoria.Historial.Count >= 6, "Auditoría conserva transiciones");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_ALTO") with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Riesgo alto bloquea incluso con OTP");
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_MEDIO"));
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "Riesgo medio exige OTP bajo 10 000");
r = await f.Caso.EjecutarAsync(Solicitud(100, "SIN_SERVICIO"));
Check(r.Status == PaymentExecutionStatus.Unavailable && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
    "Antifraude sin respuesta no debita");
r = await f.Caso.EjecutarAsync(Solicitud() with { SourceAccountNumber = "003" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Cuenta inactiva");
r = await f.Caso.EjecutarAsync(Solicitud() with { CustomerNationalId = "87654321" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Cuenta ajena");
r = await f.Caso.EjecutarAsync(Solicitud(2000) with { CustomerNationalId = "87654321", SourceAccountNumber = "002" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Saldo debe cubrir también la comisión");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "002") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Completed && r.Fee == 0 &&
    (await f.Core.ObtenerCuentaAsync("002"))!.Saldo == 2100, "Mismo banco acredita y no cobra comisión");
r = await f.Caso.EjecutarAsync(Solicitud(100, "001") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Rejected, "No transferir a la misma cuenta");
r = await f.Caso.EjecutarAsync(Solicitud(100, "NO_EXISTE") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Rejected && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_900,
    "Destino interno inexistente no debita");

f = Crear();
var concurrentes = await Task.WhenAll(Enumerable.Range(0, 6)
    .Select(_ => f.Caso.EjecutarAsync(Solicitud(10_000) with { OtpCode = "123456" })));
Check(concurrentes.Count(x => x.Status == PaymentExecutionStatus.Completed) == 5 &&
    concurrentes.Count(x => x.Status == PaymentExecutionStatus.Rejected) == 1, "Límite acumulado y concurrencia");

var reloj = new RelojPrueba(new DateTimeOffset(2026, 10, 16, 4, 59, 0, TimeSpan.Zero));
f = Crear(reloj: reloj);
await f.Caso.EjecutarAsync(Solicitud(50_000) with { OtpCode = "123456" });
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Status == PaymentExecutionStatus.Rejected, "Límite exacto alcanzado");
reloj.Ahora = reloj.Ahora.AddMinutes(2);
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Status == PaymentExecutionStatus.Completed, "Límite se reinicia a medianoche de Lima");

f = Crear();
s = Solicitud(100, "CAMARA_TIMEOUT");
r = await f.Caso.EjecutarAsync(s);
var saldo = (await f.Core.ObtenerCuentaAsync("001"))!.Saldo;
await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Pending && saldo == 99_895 &&
    (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == saldo, "Resultado incierto queda pendiente sin repetir débito");

f = Crear(new NotificadorFallido());
r = await f.Caso.EjecutarAsync(Solicitud());
Check(r.Status == PaymentExecutionStatus.Completed && !r.CustomerNotified, "Fallo de notificación conserva éxito");

// Dos handlers simulan dos solicitudes con ámbitos distintos que comparten el coordinador de operaciones.
var coreCompartido = new CoreBancarioAdapter();
var auditoriaCompartida = new AuditoriaRepository();
var controlCompartido = new ControlOperacionMemoria();
var camaraContada = new CamaraContadora();
var antifraudeAuditado = new AntifraudeExigeRegistro(auditoriaCompartida);
var primero = Crear(antifraude: antifraudeAuditado, coreCompartido: coreCompartido,
    auditoriaCompartida: auditoriaCompartida, control: controlCompartido, camara: camaraContada);
var segundo = Crear(antifraude: antifraudeAuditado, coreCompartido: coreCompartido,
    auditoriaCompartida: auditoriaCompartida, control: controlCompartido, camara: camaraContada);
s = Solicitud();
var repetidos = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
    (i % 2 == 0 ? primero.Caso : segundo.Caso).EjecutarAsync(s)));
Check(repetidos.All(x => x.Status == PaymentExecutionStatus.Completed) &&
    (await coreCompartido.ObtenerCuentaAsync("001"))!.Saldo == 99_895,
    "Dos handlers con la misma referencia ejecutan un único débito");
Check(camaraContada.Envios == 1 && antifraudeAuditado.Evaluaciones == 1,
    "La coordinación abarca riesgo y cámara, además del débito");
Check(antifraudeAuditado.RegistroPrevioValido, "La transferencia existe en auditoría antes de solicitar antifraude");

// El mismo ID puede avanzar desde indisponibilidad a confirmación y finalmente a éxito sin generar otra operación.
var riesgoRecuperable = new AntifraudeRecuperable();
f = Crear(antifraude: riesgoRecuperable);
s = Solicitud(10_000);
r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Unavailable && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
    "Primera caída de antifraude conserva el saldo y permite reintentar");
r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.PendingConfirmation &&
    (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
    "Respuesta de riesgo recuperada exige OTP para el mismo ID");
r = await f.Caso.EjecutarAsync(s with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Completed && riesgoRecuperable.Evaluaciones == 3 &&
    (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 89_995,
    "Confirmación posterior completa una sola transferencia tras recuperarse antifraude");

foreach (var invalida in new[]
{
    Solicitud() with { CustomerNationalId = "11111111" },
    Solicitud() with { CustomerNationalId = "87654321" },
    Solicitud() with { SourceAccountNumber = "NO_EXISTE" },
    Solicitud() with { DestinationBank = "NO_EXISTE" }
})
{
    f = Crear();
    r = await f.Caso.EjecutarAsync(invalida);
    Check(r.Status == PaymentExecutionStatus.Rejected && !r.CustomerNotified &&
        await f.Auditoria.ObtenerAsync(invalida.TransactionReference) is null && f.Auditoria.Historial.Count == 0,
        "Cliente, titular, cuenta o banco sin resolver se rechaza sin inventar referencias de auditoría");
}

f = Crear(new NotificadorEncolado());
s = Solicitud();
r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Completed && !r.CustomerNotified &&
    (await f.Auditoria.ObtenerAsync(s.TransactionReference)) is { Notificada: false },
    "Encolar un aviso sin confirmar envío conserva Completed y CustomerNotified=false");

foreach (var invalida in new[]
{
    Solicitud() with { CustomerNationalId = "١٢٣٤٥٦٧٨" },
    Solicitud(10_000_000_000_000_000m)
})
{
    f = Crear();
    var rechazada = false;
    try { await f.Caso.EjecutarAsync(invalida); }
    catch (ArgumentException) { rechazada = true; }
    Check(rechazada && f.Auditoria.Historial.Count == 0 &&
        (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
        "El contrato rechaza DNI Unicode e importes fuera de decimal(18,2) antes de tocar persistencia");
}

// Una confirmación al día siguiente conserva la fecha de solicitud, pero utiliza el límite del día del débito.
reloj = new RelojPrueba(new DateTimeOffset(2026, 10, 17, 4, 59, 0, TimeSpan.Zero));
f = Crear(reloj: reloj);
s = Solicitud(10_000);
await f.Caso.EjecutarAsync(s);
var fechaOriginal = (await f.Auditoria.ObtenerAsync(s.TransactionReference))!.Fecha;
reloj.Ahora = reloj.Ahora.AddMinutes(2);
r = await f.Caso.EjecutarAsync(s with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Completed &&
    (await f.Auditoria.ObtenerAsync(s.TransactionReference))!.Fecha == fechaOriginal,
    "La confirmación OTP conserva la fecha original de la solicitud al cruzar medianoche");
Check(await f.Core.ObtenerAcumuladoAsync("12345678", new DateOnly(2026, 10, 16)) == 0 &&
    await f.Core.ObtenerAcumuladoAsync("12345678", new DateOnly(2026, 10, 17)) == 10_000,
    "El débito confirmado después de medianoche consume el límite del día actual de Lima");
Console.WriteLine($"Completadas: {total} comprobaciones.");

// Adaptador de prueba: devuelve la transferencia y traduce 400 y 409 sin cuerpo a las excepciones que esperan las comprobaciones.
sealed class CasoPrueba(IRealizarTransferencia handler)
{
    static readonly TraceIdentifierAdapter Trace = new() { MessageIdentifier = "prueba", DeviceIdentifier = "prueba", ChannelIdentifier = "prueba" };

    public async Task<InitiatePaymentExecutionAdapter> EjecutarAsync(InitiatePaymentExecutionRequest s)
    {
        var r = await handler.InitiatePaymentExecutionAsync(Trace, s);
        if (r.SuccessValue is not null) return r.SuccessValue;
        throw r.Status == 400 ? new ArgumentException(r.ValidationValues.First().Message)
            : new InvalidOperationException(r.ValidationValues.First().Message);
    }
}

sealed class NotificadorFallido : INotificadorCliente
{
    public Task<bool> NotificarAsync(Transferencia t, CancellationToken ct = default) => throw new HttpRequestException();
}

sealed class NotificadorEncolado : INotificadorCliente
{
    public Task<bool> NotificarAsync(Transferencia t, CancellationToken ct = default) => Task.FromResult(false);
}

sealed class AntifraudeExigeRegistro(IAuditoriaRepository auditoria) : IAntifraudeService
{
    private int _evaluaciones;
    public int Evaluaciones => _evaluaciones;
    public bool RegistroPrevioValido { get; private set; }

    public async Task<NivelRiesgo> EvaluarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _evaluaciones);
        var registrada = await auditoria.ObtenerAsync(transferencia.Id, ct);
        RegistroPrevioValido = registrada is not null && registrada.Id == transferencia.Id &&
            registrada.Estado == EstadoTransferencia.NoDisponible && registrada.Monto == transferencia.Monto &&
            registrada.CuentaOrigen == transferencia.CuentaOrigen;
        if (!RegistroPrevioValido)
            throw new InvalidOperationException("El evaluador no puede referenciar una transferencia inexistente o incompatible.");
        await Task.Delay(30, ct);
        return NivelRiesgo.Bajo;
    }
}

sealed class AntifraudeRecuperable : IAntifraudeService
{
    private int _evaluaciones;
    public int Evaluaciones => _evaluaciones;

    public Task<NivelRiesgo> EvaluarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _evaluaciones) == 1)
            throw new HttpRequestException("Indisponibilidad temporal del evaluador de prueba.");
        return Task.FromResult(NivelRiesgo.Bajo);
    }
}

sealed class CamaraContadora : ICamaraCompensacionGateway
{
    private int _envios;
    public int Envios => _envios;

    public async Task EnviarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _envios);
        await Task.Delay(5, ct);
    }
}

sealed class RelojPrueba(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora;
}
