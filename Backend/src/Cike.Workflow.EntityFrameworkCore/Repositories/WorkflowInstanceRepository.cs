using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Core.Serialization;
using Cike.Workflow.Domain.Filters;
using Microsoft.Extensions.Logging;
using System.Linq.Dynamic.Core;

namespace Cike.EntityFrameworkCore.Repositories;

public class WorkflowInstanceRepository(CikeWorkflowDbContext context, IWorkflowStateSerializer workflowStateSerializer, ILogger<WorkflowInstanceRepository> logger)
    : SerializedEfCoreRepository<CikeWorkflowDbContext, WorkflowInstance>(context), IWorkflowInstanceRepository, IScopedDependency
{
    protected override ValueTask OnSaveAsync(WorkflowInstance entity, CancellationToken cancellationToken)
    {
        DbContext.Entry(entity).Property("SerializedWorkflowState").CurrentValue = workflowStateSerializer.Serialize(entity.WorkflowState);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLoadAsync(WorkflowInstance? entity, CancellationToken cancellationToken)
    {
        if (entity == null)
            return ValueTask.CompletedTask;

        var json = (string?)DbContext.Entry(entity).Property("SerializedWorkflowState").CurrentValue;

        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                entity.WorkflowState = workflowStateSerializer.Deserialize<WorkflowState>(json);
        }
        catch (Exception exp)
        {
            logger.LogError(exp, "Could not deserialize workflow instance state: {DefinitionId}. Reverting to default state", entity.DefinitionId);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<WorkflowInstance?> FindAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default)
    {
        var list = await FindManyAsync(filter, cancellationToken);
        return list.FirstOrDefault();
    }

    public async ValueTask<IEnumerable<WorkflowInstance>> FindManyAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default)
    {
        var query = filter.Apply(GetQueryable());
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<(long Total, List<WorkflowInstance> Items)> GetPagedListAsync(WorkflowInstanceFilter filter, string? sorting, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = filter.Apply(GetQueryable()).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sorting))
            query = query.OrderBy(sorting);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (total, items);
    }
}
