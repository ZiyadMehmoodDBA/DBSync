using FluentValidation;
using MSOSync.Api.Dtos.Operations;

namespace MSOSync.Api.Validators;

public sealed class CreateRollingOperationRequestValidator : AbstractValidator<CreateRollingOperationRequest>
{
    private static readonly string[] Kinds = ["RollingMaintenance", "RollingUpgrade"];
    private static readonly string[] WaveActions = ["manual-confirm", "auto-window"];

    public CreateRollingOperationRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => Kinds.Contains(k))
            .WithMessage("Kind must be RollingMaintenance or RollingUpgrade");
        RuleFor(x => x.NodeIds).NotEmpty();
        RuleForEach(x => x.NodeIds).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WaveSize).GreaterThan(0).When(x => x.WaveSize is not null);
        RuleFor(x => x.WavePercent).InclusiveBetween(1, 100).When(x => x.WavePercent is not null);
        RuleFor(x => x).Must(x => x.WaveSize is not null || x.WavePercent is not null)
            .WithMessage("WaveSize or WavePercent is required");
        RuleFor(x => x.GateSoakSeconds).InclusiveBetween(0, 3600);
        RuleFor(x => x.WaveAction).Must(a => WaveActions.Contains(a))
            .WithMessage("WaveAction must be manual-confirm or auto-window");
        RuleFor(x => x.WindowSeconds).GreaterThan(0)
            .When(x => x.WaveAction == "auto-window")
            .WithMessage("WindowSeconds is required for auto-window");
        RuleFor(x => x.TargetVersion).NotEmpty()
            .When(x => x.Kind == "RollingUpgrade")
            .WithMessage("TargetVersion is required for RollingUpgrade");
        RuleFor(x => x.VerificationTimeoutSeconds).InclusiveBetween(30, 86400);
    }
}
