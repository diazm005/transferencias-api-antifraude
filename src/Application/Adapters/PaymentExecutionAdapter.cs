namespace Application.Adapters;

public enum PaymentExecutionStatus { Rejected, PendingConfirmation, Unavailable, Pending, Completed }

/// <summary>
/// Common payment execution data shared by the initiate and retrieve actions.
/// </summary>
public abstract class PaymentExecutionAdapter
{
    public Guid TransactionReference { get; init; }
    public string CustomerNationalId { get; init; } = "";
    public string SourceAccountNumber { get; init; } = "";
    public string DestinationAccountNumber { get; init; } = "";
    public string DestinationBank { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Fee { get; init; }
    public DateTimeOffset ExecutionDateTime { get; init; }
    public PaymentExecutionStatus Status { get; init; }
    public string StatusMessage { get; init; } = "";
    public bool CustomerNotified { get; init; }
}

/// <summary>Result of initiating a payment execution. Serialized as InitiatePaymentExecutionResponse.</summary>
public sealed class InitiatePaymentExecutionAdapter : PaymentExecutionAdapter;

/// <summary>Last audited state of a payment execution. Serialized as RetrievePaymentExecutionResponse.</summary>
public sealed class RetrievePaymentExecutionAdapter : PaymentExecutionAdapter;
