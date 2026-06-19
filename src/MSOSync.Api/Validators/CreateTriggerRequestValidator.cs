using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateTriggerRequestValidator : AbstractValidator<CreateTriggerRequest>
{
    public CreateTriggerRequestValidator()
    {
        RuleFor(x => x.TriggerId)
            .NotEmpty().WithMessage("TriggerId is required")
            .MaximumLength(50).WithMessage("TriggerId must be at most 50 characters")
            .Matches(@"^[a-z0-9_\-]+$").WithMessage("TriggerId must contain only lowercase letters, digits, underscores, and hyphens");

        RuleFor(x => x.SourceTable)
            .NotEmpty().WithMessage("SourceTable is required")
            .MaximumLength(128).WithMessage("SourceTable must be at most 128 characters");

        RuleFor(x => x.ChannelId)
            .NotEmpty().WithMessage("ChannelId is required")
            .MaximumLength(50).WithMessage("ChannelId must be at most 50 characters");
    }
}
