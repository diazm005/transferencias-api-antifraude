namespace Application.Adapters;

/// <summary>
/// Account data and available balance returned by the retrieve action. Serialized as RetrieveAccountResponse.
/// </summary>
public sealed class RetrieveAccountAdapter
{
    public required string AccountNumber { get; init; }
    public required string CustomerNationalId { get; init; }
    public required string Bank { get; init; }
    public required bool Active { get; init; }
    public required decimal AvailableBalance { get; init; }
}
