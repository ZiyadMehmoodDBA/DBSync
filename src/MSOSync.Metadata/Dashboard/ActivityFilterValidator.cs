using FluentValidation;

namespace MSOSync.Metadata.Dashboard;

public sealed class ActivityFilterValidator : AbstractValidator<ActivityFilter>
{
    public ActivityFilterValidator()
    {
        RuleFor(f => f.Limit).InclusiveBetween(1, 200);
        RuleFor(f => f.Type)
            .Must(t => t is null or "audit" or "batch_error")
            .WithMessage("type must be 'audit', 'batch_error', or omitted.");
    }
}
