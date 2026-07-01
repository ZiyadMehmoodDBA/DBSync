using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateNodeRequestValidator : AbstractValidator<CreateNodeRequest>
{
    public CreateNodeRequestValidator()
    {
        RuleFor(x => x.NodeId)
            .NotEmpty().WithMessage("NodeId is required")
            .MaximumLength(50).WithMessage("NodeId must be at most 50 characters")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("NodeId may only contain alphanumeric characters, hyphens, and underscores");

        RuleFor(x => x.GroupId)
            .NotEmpty().WithMessage("GroupId is required");

        RuleFor(x => x.SyncUrl)
            .NotEmpty().WithMessage("SyncUrl is required")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("SyncUrl must be a valid absolute URL");

        RuleFor(x => x.HeartbeatInterval)
            .InclusiveBetween(1, 1440).WithMessage("HeartbeatInterval must be between 1 and 1440 seconds");

        RuleFor(x => x.TransportMode)
            .IsInEnum().WithMessage("TransportMode must be a valid enum value");

        RuleFor(x => x.DbAuthMode)
            .Must(mode => mode == null || mode == "Windows" || mode == "Sql")
            .WithMessage("DbAuthMode must be 'Windows' or 'Sql' when provided");

        RuleFor(x => x.DbUser)
            .NotEmpty().WithMessage("DbUser is required when DbAuthMode is 'Sql'")
            .When(x => x.DbAuthMode == "Sql");

        RuleFor(x => x.DbPassword)
            .MaximumLength(500).WithMessage("DbPassword must be at most 500 characters")
            .When(x => x.DbPassword != null);
    }
}
