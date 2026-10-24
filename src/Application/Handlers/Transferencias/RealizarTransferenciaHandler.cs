using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Handlers.Transferencias;

// La capa de aplicación coordina el flujo; la capa de dominio define las reglas y los contratos.
public sealed class RealizarTransferenciaHandler(
    IConsultarClienteRepository customers, IConsultarCuentaRepository accounts,
    ICoreBancarioGateway core, IAntifraudeService antifraud,
    ISegmentoLimiteRepository limits, IOtpService otp,
    ICamaraCompensacionGateway clearingHouse, IAuditoriaRepository audit,
    INotificadorCliente notifier, TimeProvider clock,
    ILogger<RealizarTransferenciaHandler> logger,
    IConsultarBancoRepository banks, IControlOperacion operationControl) : IRealizarTransferencia
{
    private static readonly InitiatePaymentExecutionRequestValidator Validator = new();

    /// <summary>
    /// Valida la entrada y reserva la referencia durante todo el flujo para impedir ejecuciones duplicadas.
    /// </summary>
    public async Task<EasyResult<InitiatePaymentExecutionAdapter>> InitiatePaymentExecutionAsync(
        TraceIdentifierAdapter trace, InitiatePaymentExecutionRequest request, CancellationToken ct = default)
    {
        using var _ = logger.BeginTraceScope(trace, request.TransactionReference);
        logger.LogInformation("Solicitud de transferencia recibida.");
        var validation = Validator.Validate(request);
        if (!validation.IsValid)
        {
            logger.LogWarning("Solicitud inválida.");
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(400, validation.Errors
                .Select(e => new ValidationResultAdapter { Code = "400", Message = e.ErrorMessage, Field = e.PropertyName })
                .ToList());
        }

        await using var turn = await operationControl.AdquirirAsync(request.TransactionReference, ct);
        return await ProcessAsync(request, ct);
    }

    /// <summary>Consulta las entidades, aplica las reglas del dominio y coordina riesgo, OTP y movimiento.</summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> ProcessAsync(InitiatePaymentExecutionRequest request, CancellationToken ct)
    {
        var stored = await audit.ObtenerAsync(request.TransactionReference, ct);

        var previous = GetPreviousResult(request, stored);
        if (previous is not null) return previous;

        var now = clock.GetUtcNow();
        var date = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-5)).DateTime);
        var transfer = stored ?? new Transferencia(request.TransactionReference, request.CustomerNationalId, request.SourceAccountNumber,
            request.DestinationAccountNumber, request.DestinationBank, request.Amount, 0, now, EstadoTransferencia.Rechazada, "");

        var customer = await customers.ObtenerClienteAsync(request.CustomerNationalId, ct);
        var account = await accounts.ObtenerCuentaAsync(request.SourceAccountNumber, ct);
        // Sin entidades válidas no se puede generar una transferencia con claves foráneas. El rechazo queda en el log.
        if (customer is null || account is null || account.ClienteDni != request.CustomerNationalId)
            return RejectUnresolved(transfer, "Cliente o cuenta de origen inválidos.");
        if (!await banks.ExisteAsync(request.DestinationBank, ct))
            return RejectUnresolved(transfer, "El banco de destino no está habilitado.");
        var internalTransfer = string.Equals(account.Banco, request.DestinationBank, StringComparison.OrdinalIgnoreCase);
        if (internalTransfer && request.SourceAccountNumber == request.DestinationAccountNumber)
            return RejectUnresolved(transfer, "Las cuentas de origen y destino deben ser diferentes.");
        if (internalTransfer)
        {
            var destination = await accounts.ObtenerCuentaAsync(request.DestinationAccountNumber, ct);
            if (destination is null || !destination.Activa)
                return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.Rechazada, Mensaje = "Cuenta de destino inválida." });
        }

        transfer = transfer with { Estado = EstadoTransferencia.Rechazada, Notificada = false,
            Comision = stored?.Comision ?? CalculadoraDeComision.Calcular(account.Banco, request.DestinationBank) };
        var limit = await limits.ObtenerLimiteAsync(customer.Segmento, ct);
        var accumulated = await core.ObtenerAcumuladoAsync(customer.Dni, date, ct);
        var error = Reglas.Validar(account, request.Amount, transfer.Comision, accumulated, limit);
        if (error is not null) return await FinalizeAsync(transfer with { Mensaje = error });

        // El seguimiento de fraude referencia una transferencia existente. NoDisponible permite reintentar sin mover dinero.
        await audit.GuardarAsync(transfer with { Estado = EstadoTransferencia.NoDisponible,
            Mensaje = "Evaluación antifraude pendiente. No se ha debitado dinero." }, ct);
        var risk = await EvaluateRiskAsync(transfer, ct);
        if (risk is null)
            return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.NoDisponible,
                Mensaje = "Antifraude no disponible. No se ha debitado dinero." });
        if (risk == NivelRiesgo.Alto)
            return await FinalizeAsync(transfer with { Mensaje = "Operación bloqueada por riesgo alto." });
        if (Reglas.RequiereOtp(request.Amount, risk.Value) &&
            !await otp.VerificarAsync(request.CustomerNationalId, request.TransactionReference, request.OtpCode, ct))
            return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.RequiereConfirmacion,
                Mensaje = "Se requiere un OTP válido para confirmar la operación." });

        ct.ThrowIfCancellationRequested();
        // La espera de antifraude puede cruzar medianoche: el débito consume el día efectivo del movimiento.
        var debitDate = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-5)).DateTime);
        return await ExecuteMovementAsync(transfer, account, debitDate, limit);
    }

    /// <summary>
    /// Reutiliza el resultado registrado; rechaza una referencia con otros datos y permite reintentos sin movimiento previo.
    /// </summary>
    private EasyResult<InitiatePaymentExecutionAdapter>? GetPreviousResult(InitiatePaymentExecutionRequest request, Transferencia? previous)
    {
        if (previous is null) return null;
        if (previous.ClienteDni != request.CustomerNationalId || previous.CuentaOrigen != request.SourceAccountNumber ||
            previous.CuentaDestino != request.DestinationAccountNumber ||
            !string.Equals(previous.BancoDestino, request.DestinationBank, StringComparison.OrdinalIgnoreCase) ||
            previous.Monto != request.Amount)
        {
            logger.LogWarning("ID de operación reutilizado con otros datos.");
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(409, "409", "El ID de operación ya se usó con otros datos.");
        }
        // Solo una operación que aún no movió dinero puede volver a evaluarse.
        if (previous.Estado is EstadoTransferencia.RequiereConfirmacion or EstadoTransferencia.NoDisponible) return null;
        logger.LogInformation("Operación ya registrada con estado {Status}; se reutiliza el resultado.", previous.Estado);
        return Respond(previous);
    }

    /// <summary>
    /// Acota a cinco segundos la consulta de riesgo. El adaptador registra el fallo antes de terminar si hay cancelación.
    /// </summary>
    private async Task<NivelRiesgo?> EvaluateRiskAsync(Transferencia transfer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        // Los adaptadores respetan el token; esperamos su cierre de auditoría para no dejar tareas fuera del bloqueo de operación.
        try { return await antifraud.EvaluarAsync(transfer, timeout.Token); }
        catch (Exception ex) when (ex is TimeoutException or HttpRequestException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Antifraude no disponible.");
            return null;
        }
    }

    /// <summary>
    /// Registra el intento financiero, debita y envía a la cámara. Desde aquí una desconexión HTTP no cancela el registro.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> ExecuteMovementAsync(
        Transferencia transfer, Cuenta account, DateOnly date, decimal limit)
    {
        transfer = transfer with { Estado = EstadoTransferencia.Pendiente, Mensaje = "Operación pendiente de conciliación." };
        await audit.GuardarAsync(transfer, CancellationToken.None);
        string? error;
        try
        {
            error = await core.DebitarAsync(transfer, date, limit, CancellationToken.None);
            if (error is null && !string.Equals(account.Banco, transfer.BancoDestino, StringComparison.OrdinalIgnoreCase))
                await clearingHouse.EnviarAsync(transfer, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Agotar el tiempo de espera no prueba que el banco rechazó la transferencia: no reenviar ni devolver saldo a ciegas.
            logger.LogError(ex, "Resultado incierto al debitar o enviar a la cámara; la operación queda pendiente.");
            return await FinalizeAsync(transfer);
        }
        if (error is not null)
            return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.Rechazada, Mensaje = error });
        return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.Completada, Mensaje = "Transferencia completada." });
    }

    /// <summary>
    /// Guarda el resultado y solicita la notificación; una falla de mensajería no cambia el estado financiero.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> FinalizeAsync(Transferencia transfer)
    {
        await audit.GuardarAsync(transfer, CancellationToken.None);
        try
        {
            transfer = transfer with { Notificada = await notifier.NotificarAsync(transfer, CancellationToken.None) };
        }
        catch (Exception ex)
        {
            // Fallar al notificar no convierte una transferencia completada en rechazada.
            logger.LogWarning(ex, "No se pudo notificar al cliente.");
        }
        await audit.GuardarAsync(transfer, CancellationToken.None);
        logger.LogInformation("Transferencia finalizada con estado {Status}.", transfer.Estado);
        return Respond(transfer);
    }

    /// <summary>
    /// Traduce el estado de negocio al código HTTP y conserva el contrato de respuesta de la API.
    /// </summary>
    private static EasyResult<InitiatePaymentExecutionAdapter> Respond(Transferencia transfer) =>
        EasyResult<InitiatePaymentExecutionAdapter>.Success(transfer.ToAdapter<InitiatePaymentExecutionAdapter>(), transfer.Estado switch
        {
            EstadoTransferencia.Completada => 200,
            EstadoTransferencia.Pendiente => 202,
            EstadoTransferencia.NoDisponible => 503,
            EstadoTransferencia.RequiereConfirmacion => 409,
            _ => 422
        });

    private EasyResult<InitiatePaymentExecutionAdapter> RejectUnresolved(Transferencia transfer, string message)
    {
        logger.LogWarning("Solicitud rechazada antes del registro financiero: {Motivo}", message);
        return Respond(transfer with { Estado = EstadoTransferencia.Rechazada, Mensaje = message, Notificada = false });
    }
}
