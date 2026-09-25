using Cike.Workflow.Core.Serialization;

namespace Cike.Workflow.Domain.Materializers.Mappers;

public class WorkflowDefinitionMapper : ISingletonDependency
{
    private readonly VariableDefinitionMapper _variableDefinitionMapper;
    private readonly IActivitySerializer _activitySerializer;

    public WorkflowDefinitionMapper(VariableDefinitionMapper variableDefinitionMapper, IActivitySerializer activitySerializer)
    {
        _variableDefinitionMapper = variableDefinitionMapper;
        _activitySerializer = activitySerializer;
    }

    public WorkflowActivity Map(WorkflowDefinition source)
    {
        // 画布内容经 IActivitySerializer 多态反序列化（与序列化对称）；裸 JsonHelper 无法反序列化抽象 Activity
        var root = _activitySerializer.Deserialize<IActivity>(source.OriginalStringData!);

        var variables = source.Options?.Variables?.Select(v => _variableDefinitionMapper.Map(v)).Where(e => e != null).ToList() ?? new List<Core.Variables.Variable>();

        return new(root,
            variables!,
            source.Options!.Inputs,
            source.Options.Outputs,
            source.Options.Outcomes,
            source.Options.CustomProperties,
            source.IsReadonly,
            source.IsSystem,
            new WorkflowDefinitionInfo()
            {
                Id = source.Id,
                DefinitionId = source.DefinitionId,
                IsReadonly = source.IsReadonly,
                Description = source.Description,
                IsLatest = source.IsLatest,
                IsPublished = source.IsPublished,
                Name = source.Name,
                TenantId = source.TenantId,
                UsableAsActivity = source.UsableAsActivity,
                Version = source.Version
            });
    }
}
