namespace MSOSync.Metadata.Operations.Rolling;

public sealed record RollingOperationPolicy(
    int?    WaveSize,
    int?    WavePercent,
    int     GateSoakSeconds,
    string  WaveAction,
    int?    WindowSeconds,
    string? TargetVersion,
    int     VerificationTimeoutSeconds)
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    public static string ToJson(RollingOperationPolicy policy)
        => System.Text.Json.JsonSerializer.Serialize(policy, JsonOpts);

    public static RollingOperationPolicy FromJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize<RollingOperationPolicy>(json, JsonOpts)
           ?? throw new InvalidOperationException("Invalid rolling policy json");
}
