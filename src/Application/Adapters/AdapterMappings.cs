using Domain.Entities.Internals;
namespace Application.Adapters;

/// <summary>
/// Maps domain entities (Spanish) to the English BIAN adapters exposed by the API.
/// </summary>
internal static class AdapterMappings
{
    public static RetrieveCustomerAdapter ToAdapter(this Cliente c) =>
        new() { NationalId = c.Dni, FullName = c.Nombres, Segment = c.Segmento };

    public static RetrieveAccountAdapter ToAdapter(this Cuenta c) => new()
    {
        AccountNumber = c.Numero, CustomerNationalId = c.ClienteDni, Bank = c.Banco,
        Active = c.Activa, AvailableBalance = c.Saldo
    };

    public static T ToAdapter<T>(this Transferencia t) where T : PaymentExecutionAdapter, new() => new()
    {
        TransactionReference = t.Id, CustomerNationalId = t.ClienteDni,
        SourceAccountNumber = t.CuentaOrigen, DestinationAccountNumber = t.CuentaDestino,
        DestinationBank = t.BancoDestino, Amount = t.Monto, Fee = t.Comision,
        ExecutionDateTime = t.Fecha, StatusMessage = t.Mensaje, CustomerNotified = t.Notificada,
        Status = t.Estado switch
        {
            EstadoTransferencia.Completada => PaymentExecutionStatus.Completed,
            EstadoTransferencia.Pendiente => PaymentExecutionStatus.Pending,
            EstadoTransferencia.NoDisponible => PaymentExecutionStatus.Unavailable,
            EstadoTransferencia.RequiereConfirmacion => PaymentExecutionStatus.PendingConfirmation,
            _ => PaymentExecutionStatus.Rejected
        }
    };
}
