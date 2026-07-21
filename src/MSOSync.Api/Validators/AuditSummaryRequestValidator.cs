using FluentValidation;
using MSOSync.Api.Dtos.Audit;

namespace MSOSync.Api.Validators;

public sealed class AuditSummaryRequestValidator : AbstractValidator<AuditSummaryRequest>
{
    public AuditSummaryRequestValidator()
    {
        RuleFor(r => r.From)
            .Must((r, from) => from < r.To)
            .WithMessage("'from' must be before 'to'");

        RuleFor(r => r.To)
            .Must((r, to) => (to - r.From).TotalDays <= 365)
            .When(r => r.From < r.To)
            .WithMessage("Date range cannot exceed 365 days.");
    }
}
