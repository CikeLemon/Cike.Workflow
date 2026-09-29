using Cike.AspNetCore.MinimalAPIs.EndpointFilters;
using Cike.Contracts.EntityDtos;
using Cike.EventBus.Local;
using Cike.Workflow.Application.Contracts.WorkflowInstances;
using Cike.Workflow.Application.WorkflowInstances.Commands;
using Cike.Workflow.Application.WorkflowInstances.Queries;
using Cike.Workflow.Core.Contexts.Models;
using Cike.Workflow.Domain.Filters;
using Cike.Workflow.Runtime.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cike.Workflow.Service.Open.Services;

[AutoValidation]
public class WorkflowInstanceService : MinimalApiServiceBase
{
    /// <summary>
    /// 分页查询工作流实例。过滤条件含集合与时间戳过滤，走 POST body；
    /// 分页参数走查询串。Sorting 为空时按创建时间倒序。
    /// </summary>
    public async Task<Results<Ok<PagedResultDto<WorkflowInstanceItemDto>>, BadRequest>> PostPagedListAsync(
        [FromServices] ILocalEventBus localEventBus,
        WorkflowInstanceFilter? filter,
        [AsParameters] PagedAndSortedResultRequest pageDto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageDto.Sorting))
            pageDto.Sorting = "CreatedAt desc";
        var query = new GetPagedWorkflowInstanceListQuery(filter ?? new WorkflowInstanceFilter(), pageDto);
        await localEventBus.PublishAsync(query, cancellationToken);
        return TypedResults.Ok(query.Result);
    }

    public async Task<Results<Ok<WorkflowInstanceDetailDto>, BadRequest>> GetAsync(
        [FromServices] ILocalEventBus localEventBus,
        long id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetWorkflowInstanceQuery(id);
        await localEventBus.PublishAsync(query, cancellationToken);
        return TypedResults.Ok(query.Result);
    }

    /// <summary>
    /// 对草稿版本行发起调试（试跑）：从开始节点启动，实例标记 IsDebug。
    /// 返回预生成的实例 Id；派发同步完成，返回时实例已创建并执行（未挂起即到终态）。
    /// </summary>
    public async Task<Results<Ok<long>, BadRequest>> PostDebugRunAsync(
        [FromServices] ILocalEventBus localEventBus,
        long id,
        RunDebugWorkflowDto? dto,
        CancellationToken cancellationToken = default)
    {
        var command = new RunDebugWorkflowCommand(id, dto?.Input);
        await localEventBus.PublishAsync(command, cancellationToken);
        return TypedResults.Ok(command.WorkflowInstanceId);
    }

    /// <summary>
    /// 取消运行中的工作流实例：派发取消事件，引擎清空书签并将状态落库为 Cancelled，
    /// 实时通道补推取消终态。取消经后台事件异步执行，返回时状态尚未变更，前端按实例详情轮询；
    /// 非运行中实例静默忽略（幂等），实例不存在返回 400。
    /// </summary>
    public async Task<Results<Ok, BadRequest>> PostCancelAsync(
        [FromServices] ILocalEventBus localEventBus,
        long id,
        CancellationToken cancellationToken = default)
    {
        var command = new CancelWorkflowCommand(new DispatchCancelWorkflowRequest { WorkflowInstanceId = id });
        await localEventBus.PublishAsync(command, cancellationToken);
        return TypedResults.Ok();
    }

    public async Task<Results<Ok<List<WorkflowExecutionLogEntry>>, BadRequest>> GetLogsAsync(
        [FromServices] ILocalEventBus localEventBus,
        long id,
        long? activityInstanceId,
        CancellationToken cancellationToken = default)
    {
        return TypedResults.Ok(new List<WorkflowExecutionLogEntry>());
    }
}
