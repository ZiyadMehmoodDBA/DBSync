using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ProvisionPackageRequestValidator : AbstractValidator<ProvisionPackageRequest>
{
    public ProvisionPackageRequestValidator()
    {
        RuleFor(x => x.NodeId).NotEmpty();
    }
}
