using FluentValidation;

namespace MSOSync.Metadata.Lifecycle;

public sealed class MaintenanceStartRequestValidator : AbstractValidator<MaintenanceStartRequest>
{
    public MaintenanceStartRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512);
    }
}

public sealed class DecommissionRequestValidator : AbstractValidator<DecommissionRequest>
{
    public DecommissionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512);
        RuleFor(x => x.GracePeriodMinutes).GreaterThan(0).When(x => x.GracePeriodMinutes is not null);
    }
}

public sealed class DisableRequestValidator : AbstractValidator<DisableRequest>
{
    public DisableRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(512);
    }
}

public sealed class DrainRequestValidator : AbstractValidator<DrainRequest>
{
    public DrainRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

public sealed class ResumeDrainRequestValidator : AbstractValidator<ResumeDrainRequest>
{
    public ResumeDrainRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

public sealed class ActivateRequestValidator : AbstractValidator<ActivateRequest>
{
    public ActivateRequestValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BootstrapToken).NotEmpty();
        RuleFor(x => x.AgentVersion).NotEmpty().MaximumLength(50);
    }
}
