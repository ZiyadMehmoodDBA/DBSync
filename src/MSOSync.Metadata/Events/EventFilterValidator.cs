using FluentValidation;

namespace MSOSync.Metadata.Events;

public sealed class EventFilterValidator : AbstractValidator<EventFilter>
{
    public EventFilterValidator()
    {
        RuleFor(f => f.PageSize).InclusiveBetween(1, 500);
        RuleFor(f => f.To)
            .GreaterThanOrEqualTo(f => f.From!.Value)
            .When(f => f.From.HasValue && f.To.HasValue)
            .WithMessage("'To' must be greater than or equal to 'From'.");
    }
}
