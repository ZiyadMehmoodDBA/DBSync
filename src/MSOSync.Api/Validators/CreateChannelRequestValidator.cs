using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(x => x.ChannelId)
            .NotEmpty().WithMessage("ChannelId is required")
            .MaximumLength(50).WithMessage("ChannelId must be at most 50 characters");

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 1000).WithMessage("Priority must be between 1 and 1000");

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, 10000).WithMessage("BatchSize must be between 1 and 10000");

        RuleFor(x => x.MaxBatchToSend)
            .InclusiveBetween(1, 100).WithMessage("MaxBatchToSend must be between 1 and 100");

        RuleFor(x => x.MaxDataSize)
            .InclusiveBetween(1024L, 104857600L)
            .WithMessage("MaxDataSize must be between 1024 (1KB) and 104857600 (100MB)");
    }
}
