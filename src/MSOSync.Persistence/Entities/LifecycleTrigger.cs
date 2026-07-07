namespace MSOSync.Persistence.Entities;

public enum LifecycleTrigger
{
    Manual,        // operator command
    Registration,  // registration approval flow
    Activation,    // node /activate handshake
    Recovery,      // recovery flow
    System,        // worker-initiated (drain finalize on completion)
    Timeout,       // grace-period expiry
    Migration,     // M022 conversion
}
