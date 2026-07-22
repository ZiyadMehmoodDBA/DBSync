using FluentValidation;
using MSOSync.Api.Dtos.Cluster;

namespace MSOSync.Api.Validators;

public sealed class GetHealthTrendsRequestValidator : AbstractValidator<GetHealthTrendsRequest>
{
    private static readonly HashSet<string> ValidWindows = ["1h", "6h", "24h", "7d"];

    public GetHealthTrendsRequestValidator()
    {
        RuleFor(r => r.Window)
            .Must(w => ValidWindows.Contains(w))
            .WithMessage("Window must be one of: 1h, 6h, 24h, 7d.");
    }
}
