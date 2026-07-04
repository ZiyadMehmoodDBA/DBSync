namespace MSOSync.App.Export;

public sealed class ExportOptions
{
    public int    ImmediateThreshold { get; set; } = 50_000;
    public string BasePath           { get; set; } = "exports";
    public int    RetentionHours     { get; set; } = 24;
    public int    MaxConcurrentJobs  { get; set; } = 1;
}
