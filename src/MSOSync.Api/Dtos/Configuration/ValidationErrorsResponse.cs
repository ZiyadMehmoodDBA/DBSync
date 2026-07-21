using MSOSync.Metadata.Configuration;

namespace MSOSync.Api.Dtos.Configuration;

public sealed record ValidationErrorsResponse(IReadOnlyList<ValidationError> Errors);
