namespace Application.Adapters;

/// <summary>
/// Request to initiate a payment execution (BIAN Payment Execution service domain, action Initiate).
/// </summary>
public sealed record InitiatePaymentExecutionRequest(
    Guid TransactionReference,
    string CustomerNationalId,
    string SourceAccountNumber,
    string DestinationAccountNumber,
    string DestinationBank,
    decimal Amount,
    string? OtpCode = null);
