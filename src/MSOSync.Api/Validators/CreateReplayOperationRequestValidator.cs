using FluentValidation;
using MSOSync.Api.Dtos.Requests;

namespace MSOSync.Api.Validators;

public sealed class CreateReplayOperationRequestValidator
    : AbstractValidator<CreateReplayOperationRequest>
{
    private static readonly string[] ValidModes =
        ["FailedDelivery", "MissedData", "Both"];

    public CreateReplayOperationRequestValidator()
    {
        RuleFor(r => r.NodeId).NotEmpty();
        RuleFor(r => r.ReplayMode).Must(m => ValidModes.Contains(m))
            .WithMessage("ReplayMode must be FailedDelivery, MissedData, or Both");
        RuleFor(r => r.FromTime).NotEmpty();
        RuleFor(r => r.ToTime).NotEmpty();
        RuleFor(r => r).Must(r => r.FromTime < r.ToTime)
            .WithMessage("FromTime must be before ToTime");
        RuleFor(r => r).Must(r => (r.ToTime - r.FromTime).TotalDays <= 90)
            .WithMessage("Time range cannot exceed 90 days");
        RuleFor(r => r).Must(r =>
            r.BatchIds is null || r.BatchIds.Length == 0 || r.ReplayMode == "FailedDelivery")
            .WithMessage("BatchIds can only be specified for FailedDelivery mode");
    }
}
