using FluentValidation;

namespace MSOSync.Metadata.Audit;

public sealed class AuditFilterValidator : AbstractValidator<AuditFilter>
{
    public AuditFilterValidator()
    {
        RuleFor(f => f.PageSize).InclusiveBetween(1, 500);
    }
}
