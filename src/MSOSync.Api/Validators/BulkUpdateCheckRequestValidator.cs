using FluentValidation;
using MSOSync.Api.Dtos.Marketplace;

namespace MSOSync.Api.Validators;

/// <summary>No-op validator — BulkUpdateCheckRequest has no field constraints.</summary>
public sealed class BulkUpdateCheckRequestValidator : AbstractValidator<BulkUpdateCheckRequest>
{
    public BulkUpdateCheckRequestValidator() { }
}
