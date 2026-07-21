using System.Text.Json;
using FluentValidation;
using MSOSync.Api.Dtos.Export;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.IncomingBatches;

namespace MSOSync.Api.Validators;

public sealed class CreateExportJobRequestValidator : AbstractValidator<CreateExportJobRequest>
{
    private static readonly string[] ValidResourceTypes = ["events", "incoming-batches", "audit"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CreateExportJobRequestValidator()
    {
        RuleFor(r => r.ResourceType)
            .Must(rt => ValidResourceTypes.Contains(rt))
            .WithMessage(r => $"Unknown resourceType: {r.ResourceType}");

        RuleFor(r => r.FiltersJson)
            .Must((r, json) => DeserializesToFilter(r.ResourceType, json))
            .When(r => !string.IsNullOrEmpty(r.FiltersJson) && ValidResourceTypes.Contains(r.ResourceType))
            .WithMessage(r => $"Invalid filtersJson for {r.ResourceType}");
    }

    private static bool DeserializesToFilter(string resourceType, string json)
    {
        try
        {
            return resourceType switch
            {
                "events"           => JsonSerializer.Deserialize<EventFilter>(json, JsonOptions) is not null,
                "incoming-batches" => JsonSerializer.Deserialize<IncomingBatchFilter>(json, JsonOptions) is not null,
                "audit"            => JsonSerializer.Deserialize<AuditFilter>(json, JsonOptions) is not null,
                _                  => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
