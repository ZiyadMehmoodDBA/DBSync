// Forwarding: types have moved to MSOSync.Common.Workers to avoid circular project references.
// All consumers that used MSOSync.App.Workers.IWorkerStatusRegistry continue to compile via this alias.
global using IWorkerStatusRegistry = MSOSync.Common.Workers.IWorkerStatusRegistry;
