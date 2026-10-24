namespace Application.Internals.Adapters;

public sealed class ValidationResultAdapter
{
    public required string Code { set; get; }
    public required string Message { set; get; }
    public string? Field { set; get; }
    //public IReadOnlyCollection<ValidationError> Validations { get; init; } = Array.Empty<ValidationError>();
}
