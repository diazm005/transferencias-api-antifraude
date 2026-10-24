using FluentValidation;
namespace Application.Adapters;

/// <summary>
/// Valida formato y tamaño de la entrada; las reglas que consultan datos se evalúan en el caso de uso.
/// </summary>
public sealed class InitiatePaymentExecutionRequestValidator : AbstractValidator<InitiatePaymentExecutionRequest>
{
    public InitiatePaymentExecutionRequestValidator()
    {
        RuleFor(x => x.TransactionReference).NotEmpty().WithMessage("Indica un ID de operación.");
        RuleFor(x => x.CustomerNationalId).NotEmpty().Matches(@"^[0-9]{8}$").WithMessage("El DNI debe tener 8 dígitos del 0 al 9.");
        RuleFor(x => x.SourceAccountNumber).NotEmpty().MaximumLength(30)
            .WithMessage("Indica la cuenta de origen (máximo 30 caracteres).");
        RuleFor(x => x.DestinationAccountNumber).NotEmpty().MaximumLength(30)
            .WithMessage("Indica la cuenta de destino (máximo 30 caracteres).");
        RuleFor(x => x.DestinationBank).NotEmpty().MaximumLength(20)
            .WithMessage("Indica el banco de destino (máximo 20 caracteres).");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("El monto debe ser positivo.")
            .LessThanOrEqualTo(9_999_999_999_999_999.99m).WithMessage("El monto supera la capacidad del modelo de datos.")
            .Must(m => decimal.Round(m, 2) == m).WithMessage("El monto admite como máximo dos decimales.");
    }
}
