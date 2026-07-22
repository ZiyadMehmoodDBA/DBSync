using FluentValidation;

namespace MSOSync.Metadata.Audit;

public sealed class AuditFilterValidator : AbstractValidator<AuditFilter>
{
    public AuditFilterValidator()
    {
        RuleFor(f => f.PageSize).InclusiveBetween(1, 500);

        RuleFor(f => f.Usernames)
            .Must(a => a == null || a.Length <= 10)
            .WithMessage("Usernames filter cannot exceed 10 values.");

        RuleFor(f => f.ActionNames)
            .Must(a => a == null || a.Length <= 10)
            .WithMessage("ActionNames filter cannot exceed 10 values.");

        RuleFor(f => f.ObjectNames)
            .Must(a => a == null || a.Length <= 10)
            .WithMessage("ObjectNames filter cannot exceed 10 values.");

        RuleFor(f => f)
            .Must(f => (f.Usernames?.Length ?? 0)
                     + (f.ActionNames?.Length ?? 0)
                     + (f.ObjectNames?.Length ?? 0) <= 40)
            .OverridePropertyName("Filter")
            .WithMessage("Combined total of Usernames, ActionNames, and ObjectNames must not exceed 40.");
    }
}
