namespace MSOSync.Metadata.Operations;

public interface IOperationQueryService
{
    Task<OperationPageDto>    GetPageAsync(OperationFilter filter, CancellationToken ct);
    Task<OperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct);
}
