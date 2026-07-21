using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Dtos.Audit;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class AuditSummaryRequestValidatorTests
{
    private readonly IValidator<AuditSummaryRequest> _validator = new AuditSummaryRequestValidator();

    [Fact]
    public void Valid_Range_Passes()
    {
        var result = _validator.Validate(new AuditSummaryRequest
        {
            From = new DateTime(2026, 1, 1),
            To   = new DateTime(2026, 6, 1),
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void From_Equal_To_Fails()
    {
        var result = _validator.Validate(new AuditSummaryRequest
        {
            From = new DateTime(2026, 1, 1),
            To   = new DateTime(2026, 1, 1),
        });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "'from' must be before 'to'");
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var result = _validator.Validate(new AuditSummaryRequest
        {
            From = new DateTime(2026, 6, 1),
            To   = new DateTime(2026, 1, 1),
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Range_At_365_Days_Passes()
    {
        var from = new DateTime(2026, 1, 1);
        var result = _validator.Validate(new AuditSummaryRequest { From = from, To = from.AddDays(365) });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Range_Over_365_Days_Fails()
    {
        var from = new DateTime(2026, 1, 1);
        var result = _validator.Validate(new AuditSummaryRequest { From = from, To = from.AddDays(366) });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Date range cannot exceed 365 days.");
    }
}
