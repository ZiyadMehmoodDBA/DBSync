using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ApproveRegistrationRequestValidator : AbstractValidator<ApproveRegistrationRequest>
{
    public ApproveRegistrationRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000).When(x => x.Notes is not null);
    }
}
