using FluentValidation;

namespace MSOSync.Api.Validators;

public sealed class OperationsPageSizeValidator : AbstractValidator<int>
{
    public OperationsPageSizeValidator()
    {
        RuleFor(pageSize => pageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("pageSize must be between 1 and 100.");
    }
}
