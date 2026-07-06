using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ProvisionRequestValidator : AbstractValidator<ProvisionRequestDto>
{
    public ProvisionRequestValidator()
    {
        RuleFor(x => x.NodeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeType)
            .NotEmpty()
            .Must(t => t == "source" || t == "target")
            .WithMessage("NodeType must be 'source' or 'target'");
        RuleFor(x => x.DbServer).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DbName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupId).MaximumLength(100).When(x => x.GroupId is not null);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
