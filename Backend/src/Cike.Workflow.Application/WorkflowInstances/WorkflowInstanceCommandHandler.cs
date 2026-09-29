namespace Cike.Workflow.Application.WorkflowInstances;

internal class WorkflowInstanceCommandHandler(
    IWorkflowDispatcher workflowDispatcher,
    IStimulusDispatcher stimulusDispatcher,
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    ILock lockService,
    ISnowflakeIdGenerator identityGenerator)
{
    [LocalEventHandler]
    public async Task RunAsync(RunWorkflowCommand command, CancellationToken cancellationToken)
    {
        await workflowDispatcher.DispatchAsync(command.Request, new DispatchWorkflowOptions(), cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task RunDebugAsync(RunDebugWorkflowCommand command, CancellationToken cancellationToken = default)
    {
        var row = await GetDraftRowAsync(command.DefinitionVersionId, cancellationToken);

        command.WorkflowInstanceId = identityGenerator.NextId();

        // 定义级短锁：与保存/回滚/发布互斥，避免"草稿校验通过 → 内容被并发抽换"的窗口
        await using (await lockService.AcquireAsync(row.DefinitionId, cancellationToken))
        {
            // 锁内复查：草稿状态以持锁后读到的为准
            row = await GetDraftRowAsync(command.DefinitionVersionId, cancellationToken);

            await workflowDispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(command.DefinitionVersionId)
            {
                InstanceId = command.WorkflowInstanceId,
                Input = command.Input ?? new Dictionary<string, object>(),
                IsDebug = true,
            }, new DispatchWorkflowOptions(), cancellationToken: cancellationToken);
        }
    }

    [LocalEventHandler]
    public async Task CancelAsync(CancelWorkflowCommand command, CancellationToken cancellationToken = default)
    {
        // 取消派发经后台事件异步执行，引擎侧异常仅记日志不回传发布方；
        // 实例不存在在这里预检，给出业务语义的 400（本命令自身为同步事件，预检结果可回传）
        if (!await workflowInstanceRepository.AnyAsync(x => x.Id == command.Request.WorkflowInstanceId, cancellationToken))
            throw new UserFriendlyException("工作流实例不存在，请检查后重试。");

        await workflowDispatcher.DispatchAsync(command.Request, cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task ResumeAsync(ResumeWorkflowCommand command, CancellationToken cancellationToken = default)
    {
        await stimulusDispatcher.SendAsync(command.Request, cancellationToken);
    }

    /// <summary>调试只能对草稿版本行发起；已发布行（无草稿）直接拒绝。</summary>
    private async Task<WorkflowDefinition> GetDraftRowAsync(long definitionVersionId, CancellationToken cancellationToken)
    {
        var row = await workflowDefinitionRepository.FindAsync(definitionVersionId, cancellationToken)
            ?? throw new UserFriendlyException("工作流定义版本不存在，请检查后重试。");

        if (row.IsPublished)
            throw new UserFriendlyException("已发布版本不能直接调试，请先保存草稿后再调试。");

        return row;
    }
}
