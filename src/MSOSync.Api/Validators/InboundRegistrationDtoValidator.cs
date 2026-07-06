using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class InboundRegistrationDtoValidator : AbstractValidator<InboundRegistrationDto>
{
    public InboundRegistrationDtoValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NodeType)
            .NotEmpty()
            .Must(t => t == "source" || t == "target")
            .WithMessage("NodeType must be 'source' or 'target'");
        RuleFor(x => x.Metadata!.SchemaVersion)
            .GreaterThanOrEqualTo(1)
            .WithMessage("metadata.schemaVersion must be >= 1")
            .When(x => x.Metadata is not null);
    }
}
