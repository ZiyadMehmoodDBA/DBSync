using FluentValidation;
using MSOSync.Metadata.Users;

namespace MSOSync.Api.Validators;

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        When(x => x.NewPassword != null, () =>
        {
            RuleFor(x => x.NewPassword)
                .MinimumLength(8)
                .MaximumLength(128);
        });
    }
}
