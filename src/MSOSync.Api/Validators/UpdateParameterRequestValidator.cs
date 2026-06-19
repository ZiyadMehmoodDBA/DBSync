using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class UpdateParameterRequestValidator : AbstractValidator<UpdateParameterRequest>
{
    public UpdateParameterRequestValidator()
    {
        RuleFor(x => x.Value)
            .NotNull().WithMessage("Value is required")
            .MaximumLength(4000).WithMessage("Value must be at most 4000 characters");
    }
}
