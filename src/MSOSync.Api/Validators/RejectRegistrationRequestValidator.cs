using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class RejectRegistrationRequestValidator : AbstractValidator<RejectRegistrationRequest>
{
    public RejectRegistrationRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}
