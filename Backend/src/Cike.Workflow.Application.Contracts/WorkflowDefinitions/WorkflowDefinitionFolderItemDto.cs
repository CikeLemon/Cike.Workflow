namespace Cike.Workflow.Application.Contracts.WorkflowDefinitions;

public class WorkflowDefinitionFolderItemDto : AuditedEntityDto<long>
{
    public WorkflowDefinitionFolderBaseType Type { get; set; }

    public WorkflowDefinitionFolderBaseDto Data { get; set; } = null!;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FolderItemDto), (int)WorkflowDefinitionFolderBaseType.Folder)]
[JsonDerivedType(typeof(WorkflowDefinitionItemDto), (int)WorkflowDefinitionFolderBaseType.WorkflowDefinition)]
public class WorkflowDefinitionFolderBaseDto
{

}

public enum WorkflowDefinitionFolderBaseType
{
    Folder = 1,
    WorkflowDefinition = 2,
}

public class FolderItemDto : WorkflowDefinitionFolderBaseDto
{
    public string Name { get; set; } = null!;

    public List<FolderPathDto> Path { get; set; } = [];
}

public class WorkflowDefinitionItemDto : WorkflowDefinitionFolderBaseDto
{
    public string DefinitionId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public WorkflowDefinitionType Type { get; set; }

    public bool UsableAsActivity { get; set; }

    public int Version { get; set; }

    public bool IsLatest { get; set; }

    public int? PublishedVersion { get; set; }

    /// <summary>最新已发布版本的行 Id，从未发布过为 null（HTTP 序列化为 "0"）。</summary>
    public long? PublishedVersionId { get; set; }

    public bool IsReadonly { get; set; }

    public bool IsSystem { get; set; }

    public string MaterializerName { get; set; } = null!;
}
