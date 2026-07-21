using FluentValidation;
using MSOSync.Metadata.Export;

namespace MSOSync.Api.Validators;

public sealed class OutgoingBatchExportFilterValidator : AbstractValidator<OutgoingBatchExportFilter>
{
    public OutgoingBatchExportFilterValidator()
    {
        RuleFor(f => f.Status)
            .Must(status => OutgoingBatchExportService.IsValidStatus(status!))
            .When(f => !string.IsNullOrEmpty(f.Status))
            .WithMessage(f => $"Unknown status '{f.Status}'. Valid values: New, Sending, Acknowledged, Error, Retry.");
    }
}
