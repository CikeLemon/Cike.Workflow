using Cike.Workflow.Core.Variables;

namespace Cike.Workflow.Core.Activities;

/// <summary>
/// Represents an executable process.
/// </summary>
[Browsable(false)]
[Activity(Namespace = "Cike", Category = "Workflows", Description = "A workflow is an activity that executes its Root activity.", Type = "Workflow")]
public class WorkflowActivity : CompositeActivity<object>, ICloneable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowActivity"/> class.
    /// </summary>
    public WorkflowActivity(
        IActivity root,
        ICollection<Variable> variables,
        ICollection<InputDefinition> inputs,
        ICollection<OutputDefinition> outputs,
        ICollection<string> outcomes,
        IDictionary<string, object> customProperties,
        bool isReadonly,
        bool isSystem,
        WorkflowDefinitionInfo workflowDefinitionInfo)
    {
        Inputs = inputs;
        Outputs = outputs;
        Outcomes = outcomes;
        Variables = variables;
        CustomProperties = customProperties;
        Root = root;
        IsReadonly = isReadonly;
        IsSystem = isSystem;
        DefinitionInfo = workflowDefinitionInfo;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowActivity"/> class.
    /// </summary>
    public WorkflowActivity(IActivity root) : this()
    {
        Root = root;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowActivity"/> class.
    /// </summary>
    public WorkflowActivity()
    {
    }

    /// <summary>
    /// Gets or sets input definitions.
    /// </summary>
    public ICollection<InputDefinition> Inputs { get; set; } = new List<InputDefinition>();

    /// <summary>
    /// Gets or sets output definitions.
    /// </summary>
    public ICollection<OutputDefinition> Outputs { get; set; } = new List<OutputDefinition>();

    /// <summary>
    /// Gets or sets possible outcomes for this workflow.
    /// </summary>
    public ICollection<string> Outcomes { get; set; } = new List<string>();

    public WorkflowDefinitionInfo DefinitionInfo { get; set; } = WorkflowDefinitionInfo.Default;

    /// <summary>
    /// Make workflow definition readonly.
    /// </summary>
    public bool IsReadonly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow is a system workflow.
    /// </summary>
    public bool IsSystem { get; }

    /// <summary>
    /// Constructs a new <see cref="WorkflowActivity"/> from the specified <see cref="IActivity"/>.
    /// </summary>
    public static WorkflowActivity FromActivity(IActivity root) => root as WorkflowActivity ?? new(root);

    /// <summary>
    /// Creates a new memory register initialized with this workflow's variables.
    /// </summary>
    public MemoryRegister CreateRegister()
    {
        return new MemoryRegister();
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (ResultVariable != null)
            context.WorkflowExecutionContext.MemoryRegister.Declare(ResultVariable);

        await base.ExecuteAsync(context);
    }

    /// <summary>
    /// Create a shallow copy of this workflow.
    /// </summary>
    public WorkflowActivity Clone() => (WorkflowActivity)((ICloneable)this).Clone();

    object ICloneable.Clone() => MemberwiseClone();
}

public class WorkflowDefinitionInfo
{
    public static WorkflowDefinitionInfo Default = new WorkflowDefinitionInfo()
    {
        Id = 1,
        DefinitionId = "1",
        Version = 1,
        TenantId = 0,
        IsLatest = true,
        IsPublished = true,
        Name = "",
    };
    public long Id { get; set; }

    public string DefinitionId { get; set; } = null!;

    public int Version { get; set; }

    public long TenantId { get; set; }

    public long WorkspaceId { get; set; }

    public bool IsLatest { get; set; }

    public bool IsPublished { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public bool? UsableAsActivity { get; set; }

    public bool IsReadonly { get; set; }

    public bool IsSystem { get; }
}
