namespace MSOSync.Common.Workers;

public interface IWorkerStatusRegistry
{
    void Register(string workerName, TimeSpan expectedInterval);
    void RecordTickStart(string workerName, TickTrigger trigger = TickTrigger.Scheduled);
    void RecordTickComplete(string workerName);
    void RecordTickFailed(string workerName, Exception ex);
    WorkerStatusDto GetOne(string workerName);
    WorkerStatusDto[] GetAll();
}
