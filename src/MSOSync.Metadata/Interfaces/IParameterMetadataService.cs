using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IParameterMetadataService
{
    Task<IReadOnlyList<ParameterDto>> GetParametersAsync(CancellationToken ct = default);
    Task<ParameterDto?> GetParameterAsync(string name, CancellationToken ct = default);
    Task UpdateParameterAsync(string name, string value, CancellationToken ct = default);
    Task<IReadOnlyList<ParameterHistoryDto>> GetParameterHistoryAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<ParameterHistoryDto>> GetAllParameterHistoryAsync(CancellationToken ct = default);
}
