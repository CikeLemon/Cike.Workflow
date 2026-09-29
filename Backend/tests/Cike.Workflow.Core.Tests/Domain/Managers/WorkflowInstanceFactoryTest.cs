using Cike.UniversalId.ULong;
using Cike.Workflow.Core.Activities;
using Cike.Workflow.Core.Activities.Abstracts;
using Cike.Workflow.Core.Models;
using Cike.Workflow.Core.Variables;
using Cike.Workflow.Domain.Managers;
using NSubstitute;

namespace Cike.Workflow.Core.Tests.Domain.Managers;

[TestFixture]
public class WorkflowInstanceFactoryTest
{
    private WorkflowInstanceFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WorkflowInstanceFactory(Substitute.For<ISnowflakeIdGenerator>());
    }

    [Test]
    public void CreateWorkflowInstance_WithDefinitionWorkspace_CopiesWorkspaceIdFromDefinitionInfo()
    {
        var workflow = CreateWorkflow(workspaceId: 1234567890);

        var instance = _factory.CreateWorkflowInstance(workflow, new WorkflowInstanceOptions());

        Assert.That(instance.WorkspaceId, Is.EqualTo(1234567890));
    }

    private static WorkflowActivity CreateWorkflow(long workspaceId)
    {
        var root = Substitute.For<IActivity>();
        return new WorkflowActivity(
            root,
            new List<Variable>(),
            new List<InputDefinition>(),
            new List<OutputDefinition>(),
            new List<string>(),
            new Dictionary<string, object>(),
            isReadonly: false,
            isSystem: false,
            new WorkflowDefinitionInfo
            {
                Id = 1,
                DefinitionId = "WF_1",
                Version = 1,
                TenantId = 0,
                WorkspaceId = workspaceId,
                IsLatest = true,
                IsPublished = true,
                Name = "test"
            });
    }
}
