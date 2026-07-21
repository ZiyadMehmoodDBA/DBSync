using FluentValidation;

namespace MSOSync.Api.Validators;

public sealed class UpsertPreferenceRequestValidator : AbstractValidator<string>
{
    public UpsertPreferenceRequestValidator()
    {
        RuleFor(key => key)
            .Must(key => !string.IsNullOrWhiteSpace(key))
            .WithMessage("Preference key must not be empty.")
            .MaximumLength(100)
            .WithMessage("Preference key must be at most 100 characters.");
    }
}
