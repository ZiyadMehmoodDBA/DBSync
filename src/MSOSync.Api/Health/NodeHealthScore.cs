namespace MSOSync.Api.Health;

public sealed record NodeHealthScore(
    string NodeId,
    string NodeName,
    int Score,
    string Grade,
    int ConnectivityScore,
    int SyncLagScore,
    int ErrorRateScore,
    int HeartbeatScore,
    DateTime ComputedAt)
{
    public static string ComputeGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 75 => "B",
        >= 50 => "C",
        >= 25 => "D",
        _ => "F",
    };
}
