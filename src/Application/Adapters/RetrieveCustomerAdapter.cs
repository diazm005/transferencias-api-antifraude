namespace Application.Adapters;

/// <summary>
/// Customer data returned by the retrieve action. Serialized as RetrieveCustomerResponse.
/// </summary>
public sealed class RetrieveCustomerAdapter
{
    public required string NationalId { get; init; }
    public required string FullName { get; init; }
    public required string Segment { get; init; }
}
