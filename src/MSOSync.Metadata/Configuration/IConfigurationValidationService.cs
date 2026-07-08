using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public interface IConfigurationValidationService
{
    Task<ValidationResult> ValidateAsync(ConfigurationSettings settings, CancellationToken ct,
        int schemaVersion = 1);
}
