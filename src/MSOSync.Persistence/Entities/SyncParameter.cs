using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
public sealed class SyncParameter : IHybridEntity
{
    public string  ParameterName   { get; set; } = null!;
    public string? ParameterValue  { get; set; }

    // ── M025: parameter metadata ───────────────────────────────────────────────
    public string? Category       { get; set; }   // e.g. FeatureFlag, Retention
    public string? DisplayName    { get; set; }
    public string? Description    { get; set; }
    public int?    DisplayOrder   { get; set; }
    public string? ValueType      { get; set; }   // Boolean|Integer|String|TimeSpan|Duration|Enum
    public string? MinimumValue   { get; set; }
    public string? MaximumValue   { get; set; }
    public string? AllowedValues  { get; set; }   // JSON array of allowed string values
    public string? DependsOn      { get; set; }   // other parameter_name this one depends on
    public string? ConflictsWith  { get; set; }   // other parameter_name this one conflicts with
    public Guid? TenantId { get; set; }  // null = system parameter; non-null = tenant custom parameter
}
