using Domain.Catalogs;
using FluentValidation;

namespace Application.Internals.Adapters;

public class TraceIdentifierAdapter
{
    public string? DeviceIdentifier  { set; get; }
    public string? MessageIdentifier { set; get; }
    public string? ChannelIdentifier { set; get; }
}

public class TraceIdentifierAdapterValidator : AbstractValidator<TraceIdentifierAdapter>
{
    public TraceIdentifierAdapterValidator()
    {
        RuleFor(x => x.DeviceIdentifier)
            .NotEmpty().WithMessage(MessageCatalog.GetErrorByCode(21002)).WithErrorCode("21002")
            .MinimumLength(5).WithMessage(MessageCatalog.GetErrorByCode(21004)).WithErrorCode("21004")
            .MaximumLength(42).WithMessage(MessageCatalog.GetErrorByCode(21005)).WithErrorCode("21005");

        RuleFor(x => x.MessageIdentifier)
            .NotEmpty().WithMessage(MessageCatalog.GetErrorByCode(21002)).WithErrorCode("21002")
            .MinimumLength(5).WithMessage(MessageCatalog.GetErrorByCode(21004)).WithErrorCode("21004")
            .MaximumLength(42).WithMessage(MessageCatalog.GetErrorByCode(21005)).WithErrorCode("21005");

        RuleFor(x => x.ChannelIdentifier)
            .NotEmpty().WithMessage(MessageCatalog.GetErrorByCode(21002)).WithErrorCode("21002")
            .MinimumLength(5).WithMessage(MessageCatalog.GetErrorByCode(21004)).WithErrorCode("21004")
            .MaximumLength(42).WithMessage(MessageCatalog.GetErrorByCode(21005)).WithErrorCode("21005");
    }
}
