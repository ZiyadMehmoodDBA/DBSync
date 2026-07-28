// src/MSOSync.Metrics/PipelineActivitySource.cs
using System.Diagnostics;

namespace MSOSync.Metrics;

public static class PipelineActivitySource
{
    public static readonly ActivitySource Source = new("MSOSync.Pipeline", "1.0");
}
