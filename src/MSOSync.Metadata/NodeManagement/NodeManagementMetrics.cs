using System.Diagnostics.Metrics;

namespace MSOSync.Metadata.NodeManagement;

public static class NodeManagementMetrics
{
    private static readonly Meter Meter = new("MSOSync.NodeManagement", "1.0.0");

    public static readonly Counter<long> RegistrationRequestsTotal = Meter.CreateCounter<long>(
        "msosync_registration_requests_total",
        description: "Total inbound registration requests");

    public static readonly Counter<long> ApprovalsTotal = Meter.CreateCounter<long>(
        "msosync_registrations_approved_total",
        description: "Total registrations approved");

    public static readonly Counter<long> RejectionsTotal = Meter.CreateCounter<long>(
        "msosync_registrations_rejected_total",
        description: "Total registrations rejected");

    public static readonly Counter<long> PackageDownloadsTotal = Meter.CreateCounter<long>(
        "msosync_provision_packages_downloaded_total",
        description: "Total provision packages downloaded");

    public static readonly Histogram<double> RegistrationDuration = Meter.CreateHistogram<double>(
        "msosync_registration_duration_seconds",
        description: "Time to process an inbound registration");

    public static readonly Histogram<double> BulkOperationDuration = Meter.CreateHistogram<double>(
        "msosync_bulk_registration_duration_seconds",
        description: "Time to process a bulk approve/reject operation");

    public static readonly Histogram<double> PackageGenerationDuration = Meter.CreateHistogram<double>(
        "msosync_provision_package_generation_seconds",
        description: "Time to generate and stream a provision package");
}
