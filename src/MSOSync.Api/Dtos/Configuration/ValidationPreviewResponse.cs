using MSOSync.Persistence.Models;

namespace MSOSync.Api.Dtos.Configuration;

public sealed record ValidationPreviewResponse(string? HashPreview, ConfigurationSettings? EffectiveSettings);
