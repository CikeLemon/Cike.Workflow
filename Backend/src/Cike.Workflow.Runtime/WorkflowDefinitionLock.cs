namespace Cike.Workflow.Runtime;

/// <summary>
/// Provides the definition-level distributed lock key used to serialize content mutations
/// (save / rollback / publish / delete) against debug run registration on a workflow definition.
/// The lock is held only for the duration of each operation (short critical section);
/// whether a debug run is "active" is always derived from instance state, never from lock possession.
/// </summary>
public static class WorkflowDefinitionLock
{
    /// <summary>
    /// The maximum time to wait for acquiring the definition lock.
    /// Operations are short; the long window is only a safety valve against a stuck holder.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns the distributed lock key for the specified workflow definition.
    /// </summary>
    public static string GetKey(string definitionId) => $"lock:workflow-definition:{definitionId}";
}
