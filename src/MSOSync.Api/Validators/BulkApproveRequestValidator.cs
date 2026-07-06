using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class BulkApproveRequestValidator : AbstractValidator<BulkApproveRequest>
{
    public BulkApproveRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("ids must contain at least one entry");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100).WithMessage("ids must not exceed 100 items");
    }
}
