// src/MSOSync.Metrics/TelemetryOptions.cs
namespace MSOSync.Metrics;

public sealed class TelemetryOptions
{
    public const string Section = "Telemetry";

    public bool Enabled { get; set; } = false;
    public string ServiceName { get; set; } = "MSOSync";
    public string ServiceVersion { get; set; } = "1.0.0";
    public string OtlpEndpoint { get; set; } = string.Empty;
}
