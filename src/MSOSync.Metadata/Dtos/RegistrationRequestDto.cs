namespace MSOSync.Metadata.Dtos;

public sealed record RegistrationRequestDto(
    long RequestId,
    string NodeId,
    string? NodeGroup,
    string? SyncUrl,
    string? NodeVersion,
    string? DbType,
    DateTime? RequestTime,
    bool Approved);
