using Cike.Workflow.Common.Serialization.Internals;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Core.Serialization.Internals;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cike.Workflow.Core.Tests.Serializers;

/// <summary>
/// 回归测试：历史上由旧版序列化器写入的状态 JSON 中，$id/$values 引用元数据不在对象首位
/// （如根对象 {"id":...,"$id":"1",...}）。读取端必须能容忍乱序元数据，
/// 否则抛 JsonException 并在仓储层回退为默认状态（实例数据丢失）。
/// 载荷为真实落库数据回放（definitionId=all-in-test，2026-10-08）。
/// </summary>
internal class WorkflowStateLegacyReferenceMetadataTest
{
    private const string LegacyJsonWithOutOfOrderMetadata = """
        {
          "id": 2108029315206615040,
          "$id": "1",
          "name": "测试所有节点",
          "input": { "input1": "test", "number1": 1 },
          "output": { "output1": "100123" },
          "status": "Finished",
          "isDebug": true,
          "isSystem": false,
          "bookmarks": { "$id": "2", "$values": [] },
          "createdAt": "2026-10-08T10:58:48.9966617+08:00",
          "incidents": { "$id": "3", "$values": [] },
          "updatedAt": "2026-10-08T10:58:49.9618727+08:00",
          "finishedAt": "2026-10-08T10:58:49.9618727+08:00",
          "properties": {},
          "isExecuting": false,
          "definitionId": "all-in-test",
          "definitionVersion": 1,
          "completionCallbacks": { "$id": "4", "$values": [] },
          "definitionVersionId": 2105187489269420032,
          "scheduledActivities": { "$id": "8", "$values": [] },
          "activityExecutionContexts": {
            "$id": "5",
            "$values": [
              {
                "id": 2108029317303767040,
                "$id": "6",
                "status": "Completed",
                "metadata": {},
                "createdAt": "2026-10-08T10:58:49.1936177+08:00",
                "faultCount": 0,
                "finishedAt": "2026-10-08T10:58:49.9605235+08:00",
                "properties": {},
                "isExecuting": false,
                "activityState": {},
                "callStackDepth": 0,
                "dynamicVariables": { "$id": "7", "$values": [] },
                "scheduledActivityNodeId": "Workflow1"
              }
            ]
          }
        }
        """;

    [Test]
    public void Deserialize_WithOutOfOrderReferenceMetadata_RestoresState()
    {
        var serializer = new JsonWorkflowStateSerializer(SerializationTypeRegistry.CreateDefault(), NullLoggerFactory.Instance);

        var restored = serializer.Deserialize<WorkflowState>(LegacyJsonWithOutOfOrderMetadata);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(2108029315206615040L));
            Assert.That(restored.DefinitionId, Is.EqualTo("all-in-test"));
            Assert.That(restored.Status, Is.EqualTo(WorkflowStatus.Finished));
            Assert.That(restored.Name, Is.EqualTo("测试所有节点"));
            Assert.That(restored.Input, Contains.Key("input1"));
            Assert.That(restored.Bookmarks, Is.Empty);
            Assert.That(restored.ActivityExecutionContexts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Serialize_ThenDeserialize_WithOutOfOrderTolerantOptions_StillRoundTrips()
    {
        // 修复手段（允许乱序元数据）不得破坏当前写端格式（$id 在首位）的正常往返。
        var serializer = new JsonWorkflowStateSerializer(SerializationTypeRegistry.CreateDefault(), NullLoggerFactory.Instance);
        var state = new WorkflowState
        {
            DefinitionId = "WF_test",
            Status = WorkflowStatus.Suspended,
            Input = new Dictionary<string, object> { ["key"] = "value" },
        };

        var json = serializer.Serialize(state);
        var restored = serializer.Deserialize<WorkflowState>(json);

        Assert.That(restored.Status, Is.EqualTo(WorkflowStatus.Suspended));
        Assert.That(restored.Input["key"], Is.EqualTo("value"));
    }
}
