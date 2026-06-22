using FluentValidation;
using MSOSync.Api.Dtos.Batches;

namespace MSOSync.Api.Validators;

public sealed class BatchListRequestValidator : AbstractValidator<BatchListRequest>
{
    private static readonly string[] AllowedSortBy = ["createTime", "batchId", "status"];

    public BatchListRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("page must be >= 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("pageSize must be between 1 and 100");

        RuleFor(x => x.SortBy)
            .Must(v => AllowedSortBy.Contains(v))
            .WithMessage($"sortBy must be one of: {string.Join(", ", AllowedSortBy)}");
    }
}
