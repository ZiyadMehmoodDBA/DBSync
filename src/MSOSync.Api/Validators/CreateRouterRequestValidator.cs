using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateRouterRequestValidator : AbstractValidator<CreateRouterRequest>
{
    private static readonly string[] ValidRouterTypes = ["default", "column", "subselect"];

    public CreateRouterRequestValidator()
    {
        RuleFor(x => x.RouterId)
            .NotEmpty().WithMessage("RouterId is required")
            .MaximumLength(50).WithMessage("RouterId must be at most 50 characters");

        RuleFor(x => x.SourceNodeGroup)
            .NotEmpty().WithMessage("SourceNodeGroup is required")
            .MaximumLength(50).WithMessage("SourceNodeGroup must be at most 50 characters");

        RuleFor(x => x.TargetNodeGroup)
            .NotEmpty().WithMessage("TargetNodeGroup is required")
            .MaximumLength(50).WithMessage("TargetNodeGroup must be at most 50 characters");

        RuleFor(x => x.RouterType)
            .NotEmpty().WithMessage("RouterType is required")
            .Must(t => ValidRouterTypes.Contains(t))
            .WithMessage("RouterType must be one of: default, column, subselect");
    }
}
