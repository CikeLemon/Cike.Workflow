using Cike.Workflow.Core.Contexts.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cike.Workflow.Core.Runners.Models;

public class WorkflowState
{
    public long Id { get; set; }

    public string DefinitionId { get; set; } = null!;

    public long DefinitionVersionId { get; set; }

    public int DefinitionVersion { get; set; }

    public long? ParentWorkflowInstanceId { get; set; }

    public string? CorrelationId { get; set; }

    public string? Name { get; set; }

    public WorkflowStatus Status { get; set; }

    public bool IsExecuting { get; set; }

    [NotMapped]
    public ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

    public ICollection<ActivityIncident> Incidents { get; set; } = new List<ActivityIncident>();

    public bool IsSystem { get; set; }

    /// <summary>调试实例标记：对草稿版本行发起的试运行。</summary>
    public bool IsDebug { get; set; }

    public ICollection<CompletionCallbackState> CompletionCallbacks { get; set; } = new List<CompletionCallbackState>();

    [NotMapped]
    public ICollection<ActivityExecutionContextState> ActivityExecutionContexts { get; set; } = new List<ActivityExecutionContextState>();

    public ICollection<ActivityWorkItemState> ScheduledActivities { get; set; } = new List<ActivityWorkItemState>();

    public IDictionary<string, object> Input { get; set; } = new Dictionary<string, object>();

    public IDictionary<string, object> Output { get; set; } = new Dictionary<string, object>();

    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
