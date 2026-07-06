namespace MSOSync.Metadata.NodeManagement;

public sealed record RegistrationMetadataDto(
    int                  SchemaVersion,
    MachineMetadata?     Machine,
    DatabaseMetadata?    Database,
    ApplicationMetadata? Application,
    HardwareMetadata?    Hardware
);

public sealed record MachineMetadata(
    string? HostName,
    string? OsVersion,
    string? MachineName
);

public sealed record DatabaseMetadata(
    string? Edition,
    string? Version,
    string? Collation,
    string? InstanceName
);

public sealed record ApplicationMetadata(
    string? AgentVersion,
    string? RuntimeVersion,
    string? InstallPath
);

public sealed record HardwareMetadata(
    int?  CpuCount,
    long? RamBytes,
    long? DiskBytes
);
