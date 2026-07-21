namespace MSOSync.Metadata.Operations.Rolling;

public interface IRollingOperationQueryService
{
    Task<RollingOperationDetailDto> GetDetailAsync(Guid operationId, CancellationToken ct = default);
}
