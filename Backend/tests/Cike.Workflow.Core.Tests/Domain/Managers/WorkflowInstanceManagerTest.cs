using System.Linq.Expressions;
using Cike.UniversalId.ULong;
using Cike.Uow;
using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Domain.Data;
using Cike.Workflow.Domain.Data.Entities;
using Cike.Workflow.Domain.Managers;
using Cike.Workflow.Domain.Managers.Mappers;
using NSubstitute;
using NSubstitute.ReceivedExtensions;

namespace Cike.Workflow.Core.Tests.Domain.Managers;

[TestFixture]
public class WorkflowInstanceManagerTest
{
    private IWorkflowInstanceRepository _repository = null!;
    private WorkflowInstanceManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IWorkflowInstanceRepository>();
        var factory = new WorkflowInstanceFactory(Substitute.For<ISnowflakeIdGenerator>());
        _manager = new WorkflowInstanceManager(_repository, factory, Substitute.For<IUnitOfWork>(), new WorkflowStateMapper());
    }

    [Test]
    public async Task SaveAsync_WhenInstanceExists_DispatchesUpdate()
    {
        // 实现已改为 FindAsync 取已跟踪实体就地应用状态（避免同键 Attach 身份冲突）
        _repository.FindAsync(42, Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstance { Id = 42 });

        await _manager.SaveAsync(new WorkflowState { Id = 42 }, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(Arg.Any<WorkflowInstance>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().InsertAsync(Arg.Any<WorkflowInstance>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveAsync_WhenInstanceNotExists_DispatchesInsert()
    {
        _repository.FindAsync(42, Arg.Any<CancellationToken>())
            .Returns((WorkflowInstance?)null);

        await _manager.SaveAsync(new WorkflowState { Id = 42 }, CancellationToken.None);

        await _repository.Received(1).InsertAsync(Arg.Any<WorkflowInstance>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<WorkflowInstance>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
