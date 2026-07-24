using FluentValidation;
using MSOSync.Api.Dtos.Marketplace;

namespace MSOSync.Api.Validators;

public sealed class MarketplaceInstallRequestValidator : AbstractValidator<MarketplaceInstallRequest>
{
    public MarketplaceInstallRequestValidator()
    {
        RuleFor(x => x.Version)
            .Matches(@"^\d+\.\d+\.\d+$")
            .WithMessage("Version must be a valid semantic version (major.minor.patch).")
            .When(x => x.Version is not null);
    }
}
