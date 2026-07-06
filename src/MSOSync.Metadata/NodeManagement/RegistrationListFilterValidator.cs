using FluentValidation;

namespace MSOSync.Metadata.NodeManagement;

public sealed class RegistrationListFilterValidator : AbstractValidator<RegistrationFilter>
{
    public RegistrationListFilterValidator()
    {
        RuleFor(f => f.PageSize).InclusiveBetween(1, 500);
    }
}
