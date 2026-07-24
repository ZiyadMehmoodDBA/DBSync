using FluentValidation;
using MSOSync.Api.Dtos.Marketplace;

namespace MSOSync.Api.Validators;

public sealed class MarketplaceSearchParamsValidator : AbstractValidator<MarketplaceSearchParams>
{
    public MarketplaceSearchParamsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Query).MaximumLength(200).When(x => x.Query is not null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
    }
}
