using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class BulkRejectRequestValidator : AbstractValidator<BulkRejectRequest>
{
    public BulkRejectRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("ids must contain at least one entry");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100).WithMessage("ids must not exceed 100 items");
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}
