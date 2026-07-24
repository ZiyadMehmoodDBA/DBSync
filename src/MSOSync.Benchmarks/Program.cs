using BenchmarkDotNet.Running;
using MSOSync.Benchmarks;

// Run: dotnet run -c Release --project src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj
// To run a single benchmark: add -- --filter *TopologyGraph* after the command

var summary = BenchmarkRunner.Run(new[]
{
    typeof(TopologyGraphBenchmark),
    typeof(NodeCursorPageBenchmark),
    typeof(BulkFanOutBenchmark),
    typeof(DashboardSummaryBenchmark),
});

Console.WriteLine("Benchmarks complete. Results in BenchmarkDotNet.Artifacts/");
