using MediatR;

namespace MSOSync.Scheduler;

public sealed record SchedulerRecoveryEvent(
    int SentRecovered,
    int RetryRequeued) : INotification;
