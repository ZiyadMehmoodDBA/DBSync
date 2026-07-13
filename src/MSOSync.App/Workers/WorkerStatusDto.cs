// Forwarding: types have moved to MSOSync.Common.Workers to avoid circular project references.
// All consumers that used MSOSync.App.Workers.* types continue to compile via these aliases.
global using WorkerState          = MSOSync.Common.Workers.WorkerState;
global using WorkerExecutionState = MSOSync.Common.Workers.WorkerExecutionState;
global using WorkerHealthState    = MSOSync.Common.Workers.WorkerHealthState;
global using TickTrigger          = MSOSync.Common.Workers.TickTrigger;
global using TickRecord           = MSOSync.Common.Workers.TickRecord;
global using WorkerStatusDto      = MSOSync.Common.Workers.WorkerStatusDto;
