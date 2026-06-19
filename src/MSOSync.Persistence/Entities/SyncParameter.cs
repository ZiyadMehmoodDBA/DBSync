namespace MSOSync.Persistence.Entities;

public sealed class SyncParameter
{
    public string ParameterName { get; set; } = null!;
    public string? ParameterValue { get; set; }
}
