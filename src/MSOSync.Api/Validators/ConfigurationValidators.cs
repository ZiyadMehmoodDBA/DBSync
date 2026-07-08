using FluentValidation;
using MSOSync.Metadata.Configuration;

namespace MSOSync.Api.Validators;

public sealed class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.InitialSettings).NotNull();
    }
}

public sealed class UpdateDraftRequestValidator : AbstractValidator<UpdateDraftRequest>
{
    public UpdateDraftRequestValidator()
    {
        RuleFor(x => x.Settings).NotNull();
    }
}

public sealed class StartRolloutRequestValidator : AbstractValidator<StartRolloutRequest>
{
    public StartRolloutRequestValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.TemplateVersion).GreaterThan(0);
        RuleFor(x => x.NodeIds).NotEmpty().WithMessage("At least one node must be specified");
    }
}

public sealed class AssignRequestValidator : AbstractValidator<AssignRequest>
{
    public AssignRequestValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
    }
}

public sealed class SetOverrideRequestValidator : AbstractValidator<SetOverrideRequest>
{
    public SetOverrideRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).NotNull();
        RuleFor(x => x.Source).Must(s => s is "Manual" or "Imported" or "API")
            .WithMessage("Source must be Manual, Imported, or API");
    }
}
