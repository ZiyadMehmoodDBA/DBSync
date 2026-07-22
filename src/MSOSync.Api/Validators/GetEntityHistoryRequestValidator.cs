using FluentValidation;
using MSOSync.Api.Dtos.Audit;

namespace MSOSync.Api.Validators;

public sealed class GetEntityHistoryRequestValidator : AbstractValidator<GetEntityHistoryRequest>
{
    public GetEntityHistoryRequestValidator()
    {
        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 500)
            .WithMessage("pageSize must be between 1 and 500.");
    }
}
