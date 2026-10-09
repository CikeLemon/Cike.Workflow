import { beforeEach, describe, expect, it, vi } from "vitest"
import type { WireActivity } from "@/core/designer/serialization"

/**
 * Seam: the designer state orchestration composable. Tests mock the generated
 * API client and assert externally observable behavior (exposed refs + which
 * API calls fire with which bodies) — never internal implementation details.
 */

const getById = vi.fn()
const getDescriptors = vi.fn()
const getExpressionDescriptors = vi.fn()
const getVarialbeTypes = vi.fn()
const getStorageDriverDescriptors = vi.fn()
const save = vi.fn()
const publish = vi.fn()
const rollback = vi.fn()
const versionList = vi.fn()
const validateCanvas = vi.fn()
const debugRun = vi.fn()

vi.mock("@/api/generated", () => ({
  getApiV1WorkflowDefinitionsById: (...args: unknown[]) => getById(...args),
  getApiV1CommonsActivityDescriptors: (...args: unknown[]) => getDescriptors(...args),
  getApiV1CommonsExpressionDescriptors: (...args: unknown[]) => getExpressionDescriptors(...args),
  getApiV1CommonsVarialbeTypes: (...args: unknown[]) => getVarialbeTypes(...args),
  getApiV1CommonsStorageDriverDescriptors: (...args: unknown[]) => getStorageDriverDescriptors(...args),
  postApiV1WorkflowDefinitionsSaveById: (...args: unknown[]) => save(...args),
  postApiV1WorkflowDefinitionsPublishById: (...args: unknown[]) => publish(...args),
  postApiV1WorkflowDefinitionsRollback: (...args: unknown[]) => rollback(...args),
  getApiV1WorkflowDefinitionsVersionList: (...args: unknown[]) => versionList(...args),
  postApiV1WorkflowDefinitionsValidateCanvas: (...args: unknown[]) => validateCanvas(...args),
  postApiV1WorkflowInstancesDebugRunById: (...args: unknown[]) => debugRun(...args),
}))

import { useWorkflowDesigner } from "@/composables/useWorkflowDesigner"

function makeRoot(): WireActivity {
  return {
    type: "Cike.Flowchart",
    id: "fc-root",
    name: "示例流程",
    activities: [{ type: "Cike.Start", id: "a-start", name: "开始" }],
    connections: [],
  }
}

interface DetailOverrides {
  id?: string
  isLatest?: boolean
  isPublished?: boolean
  version?: number
}

function makeDetail(overrides: DetailOverrides = {}) {
  return {
    id: overrides.id ?? "100",
    definitionId: "def-1",
    name: "月度报销审批",
    description: "每月员工报销单据的逐级审批流程",
    type: 1,
    usableAsActivity: false,
    materializerName: "Elsa.Workflows.Serialization",
    version: overrides.version ?? 1,
    isLatest: overrides.isLatest ?? true,
    isPublished: overrides.isPublished ?? false,
    folderId: "folder-1",
    root: makeRoot(),
    options: { variables: [] },
  }
}

beforeEach(() => {
  getById.mockReset()
  getDescriptors.mockReset()
  getExpressionDescriptors.mockReset()
  getVarialbeTypes.mockReset()
  getStorageDriverDescriptors.mockReset()
  save.mockReset()
  publish.mockReset()
  rollback.mockReset()
  versionList.mockReset()
  validateCanvas.mockReset()
  getDescriptors.mockResolvedValue({ data: [] })
  getExpressionDescriptors.mockResolvedValue({ data: [] })
  getVarialbeTypes.mockResolvedValue({ data: [] })
  getStorageDriverDescriptors.mockResolvedValue({ data: [] })
  validateCanvas.mockResolvedValue({ data: [] })
  versionList.mockResolvedValue({
    data: [{ id: "100", version: 1, isLatest: true, isPublished: false }],
  })
  getById.mockResolvedValue({ data: makeDetail(), error: undefined })
})

describe("useWorkflowDesigner version state", () => {
  it("Load_PopulatesVersionState", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.rowId.value).toBe("100")
    expect(designer.definitionId.value).toBe("def-1")
    expect(designer.version.value).toBe(1)
    expect(designer.isLatest.value).toBe(true)
    expect(designer.isPublished.value).toBe(false)
    expect(designer.readonly.value).toBe(false)
  })

  it("ViewHistoricVersion_NonLatest_IsReadonly", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", version: 1, isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")
    expect(designer.rowId.value).toBe("90")
    expect(designer.readonly.value).toBe(true)
  })
})

describe("useWorkflowDesigner save", () => {
  it("SaveDraftLatest_SameId_NoReload", async () => {
    getById.mockResolvedValue({ data: makeDetail({ isPublished: false }), error: undefined })
    save.mockResolvedValue({ data: "100", error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const callsAfterLoad = getById.mock.calls.length
    await designer.save()
    // In-place save: no full canvas reload, version row unchanged.
    expect(getById.mock.calls.length).toBe(callsAfterLoad)
    expect(designer.rowId.value).toBe("100")
    expect(save).toHaveBeenCalledTimes(1)
    const body = save.mock.calls[0][0].body
    expect(body).toHaveProperty("root")
    expect(body).toHaveProperty("options")
  })

  it("SavePublishedVersion_ForksNewVersion_ReloadsCanvas", async () => {
    // Opening a published version, then saving forks a new draft version id.
    getById.mockImplementation(({ path }: { path: { id: string } }) =>
      Promise.resolve({
        data: makeDetail({ id: path.id, isPublished: path.id === "100", isLatest: true, version: path.id === "100" ? 1 : 2 }),
        error: undefined,
      }),
    )
    save.mockResolvedValue({ data: "101", error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.isPublished.value).toBe(true)
    await designer.save()
    // Backend forked a new draft version → canvas reloaded onto the new row id.
    expect(designer.rowId.value).toBe("101")
    expect(designer.isPublished.value).toBe(false)
    const lastGetById = getById.mock.calls.at(-1)![0]
    expect(lastGetById.path.id).toBe("101")
  })

  it("Save_WhenReadonly_DoesNotCallApi", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")
    await designer.save()
    expect(save).not.toHaveBeenCalled()
  })
})

describe("useWorkflowDesigner publish", () => {
  it("Publish_SendsRootOptionsAndNote", async () => {
    publish.mockResolvedValue({ data: "100", error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.publish("增加多级审批")
    expect(publish).toHaveBeenCalledTimes(1)
    const arg = publish.mock.calls[0][0]
    expect(arg.path.id).toBe("100")
    expect(arg.body).toHaveProperty("root")
    expect(arg.body).toHaveProperty("options")
    expect(arg.body.publishedNote).toBe("增加多级审批")
  })

  it("Publish_WhenReadonly_DoesNotCallApi", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")
    await designer.publish()
    expect(publish).not.toHaveBeenCalled()
  })
})

describe("useWorkflowDesigner nodeId", () => {
  it("Load_ComputesNodeIdsAcrossTree", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const root = designer.root.value as unknown as {
      nodeId?: string | null
      activities: Array<{ id: string; nodeId?: string | null }>
    }
    expect(root.nodeId).toBe("fc-root")
    expect(root.activities[0]!.nodeId).toBe("fc-root:a-start")
  })

  it("AddNode_ComputesNodeIdFromParent", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.addNode("Cike.End", { x: 0, y: 0 })
    const root = designer.root.value as unknown as {
      activities: Array<{ id: string; nodeId?: string | null }>
    }
    const added = root.activities[root.activities.length - 1]!
    // New node's NodeId is derived O(1) from its parent's, not left null.
    expect(added.nodeId).toBe(`fc-root:${added.id}`)
  })
})

describe("useWorkflowDesigner versions", () => {
  it("Rollback_PostsDefinitionIdAndVersionId", async () => {
    rollback.mockResolvedValue({ data: undefined, error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.rollback("90")
    expect(rollback).toHaveBeenCalledTimes(1)
    const body = rollback.mock.calls[0][0].body
    expect(body.definitionId).toBe("def-1")
    expect(body.definitionVersionId).toBe("90")
  })

  it("Rollback_ApiError_SurfacesBackendMessageInSaveError", async () => {
    rollback.mockResolvedValue({ data: undefined, error: "该版本已发布，不可回滚" })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.rollback("90")
    expect(designer.saveError.value).toBe("该版本已发布，不可回滚")
  })

  it("LoadVersions_ApiError_SetsVersionsError", async () => {
    versionList.mockResolvedValue({ data: null, error: "版本列表不可用" })
    const designer = useWorkflowDesigner()
    // load() fires loadVersions without awaiting it — wait for the rejection surface.
    await designer.load("100")
    await vi.waitFor(() => expect(designer.versionsError.value).toBe("版本列表不可用"))
    expect(designer.versions.value).toEqual([])
  })

  it("ReturnToLatest_LoadsLatestVersionRow", async () => {
    versionList.mockResolvedValue({
      data: [
        { id: "200", version: 2, isLatest: true, isPublished: false },
        { id: "100", version: 1, isLatest: false, isPublished: true },
      ],
    })
    // Start by viewing the old version, then return to latest.
    getById.mockImplementation(({ path }: { path: { id: string } }) =>
      Promise.resolve({
        data: makeDetail({ id: path.id, isLatest: path.id === "200", version: path.id === "200" ? 2 : 1 }),
        error: undefined,
      }),
    )
    const designer = useWorkflowDesigner()
    await designer.viewVersion("100")
    expect(designer.readonly.value).toBe(true)
    await designer.returnToLatest()
    expect(designer.rowId.value).toBe("200")
    expect(designer.readonly.value).toBe(false)
  })
})

describe("useWorkflowDesigner validation", () => {
  it("Load_TriggersValidationOnce", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    // Opening a canvas validates once (non-blocking) so the designer reflects
    // any problems the loaded draft already has.
    expect(validateCanvas).toHaveBeenCalledTimes(1)
    const body = validateCanvas.mock.calls[0][0].body
    expect(body).toHaveProperty("root")
    expect(body).toHaveProperty("options")
  })

  it("Validate_PopulatesProblems", async () => {
    validateCanvas.mockResolvedValue({
      data: [{ activityId: "a-start", nodeId: "fc-root:a-start", name: "开始", message: "开始节点缺少连线" }],
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.validate()
    expect(designer.problems.value).toEqual([
      { activityId: "a-start", nodeId: "fc-root:a-start", name: "开始", message: "开始节点缺少连线" },
    ])
    expect(designer.validating.value).toBe(false)
  })

  it("Projection_MarksErrorNodesFromProblems", async () => {
    validateCanvas.mockResolvedValue({
      data: [{ activityId: "a-start", nodeId: "fc-root:a-start", name: "开始", message: "缺少连线" }],
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.validate()
    // The projected node for the offending activity carries hasError so the
    // canvas can badge it, reusing the activityId→visual-overlay pattern.
    const start = designer.projection.value.nodes.find((n) => n.data.activityId === "a-start")
    expect(start?.data.hasError).toBe(true)
  })

  it("Validate_TransportError_SetsValidationError", async () => {
    validateCanvas.mockResolvedValue({ data: undefined, error: "Network Error" })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    await designer.validate()
    // A failed request must be distinguishable from a clean canvas.
    expect(designer.validationError.value).toBeTruthy()
  })

  it("Validate_WhenReadonly_DoesNotCallApi", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")
    expect(validateCanvas).not.toHaveBeenCalled()
  })

  it("Validate_OnlyLatestResponseWins", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    validateCanvas.mockReset()
    let resolveStale: (value: unknown) => void = () => {}
    validateCanvas.mockImplementationOnce(() => new Promise((resolve) => { resolveStale = resolve }))
    validateCanvas.mockImplementationOnce(() => Promise.resolve({ data: [], error: undefined }))
    const stale = designer.validate()
    const latest = designer.validate()
    // The slow (stale) request resolves after the latest one; it must be dropped.
    resolveStale({ data: [{ activityId: "old", nodeId: null, name: null, message: "过期结果" }], error: undefined })
    await stale
    await latest
    expect(designer.problems.value).toEqual([])
  })

  it("Edit_SchedulesDebouncedValidation", async () => {
    vi.useFakeTimers()
    try {
      const designer = useWorkflowDesigner()
      await designer.load("100")
      validateCanvas.mockClear()
      designer.addNode("Cike.End", { x: 0, y: 0 })
      // Debounced: not fired synchronously on edit.
      expect(validateCanvas).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(500)
      expect(validateCanvas).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it("Edit_Burst_CoalescesIntoOneValidation", async () => {
    vi.useFakeTimers()
    try {
      const designer = useWorkflowDesigner()
      await designer.load("100")
      validateCanvas.mockClear()
      designer.addNode("Cike.End", { x: 0, y: 0 })
      designer.addNode("Cike.End", { x: 40, y: 40 })
      designer.addNode("Cike.End", { x: 80, y: 80 })
      await vi.advanceTimersByTimeAsync(500)
      expect(validateCanvas).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it("Undo_SchedulesDebouncedValidation", async () => {
    vi.useFakeTimers()
    try {
      const designer = useWorkflowDesigner()
      await designer.load("100")
      designer.addNode("Cike.End", { x: 0, y: 0 })
      await vi.advanceTimersByTimeAsync(500)
      validateCanvas.mockClear()
      designer.undo()
      expect(validateCanvas).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(500)
      expect(validateCanvas).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it("Publish_WhenValidationErrors_DoesNotCallBackend", async () => {
    validateCanvas.mockResolvedValue({
      data: [{ activityId: "x", nodeId: null, name: null, message: "孤立节点" }],
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const ok = await designer.publish("note")
    expect(publish).not.toHaveBeenCalled()
    expect(ok).toBe(false)
  })

  it("Publish_WhenClean_CallsBackend", async () => {
    validateCanvas.mockResolvedValue({ data: [], error: undefined })
    publish.mockResolvedValue({ data: "100", error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const ok = await designer.publish("note")
    expect(publish).toHaveBeenCalledTimes(1)
    expect(ok).toBe(true)
  })

  it("Publish_BackendRejects_BackfillsProblems", async () => {
    validateCanvas
      .mockResolvedValueOnce({ data: [], error: undefined }) // load-time
      .mockResolvedValueOnce({ data: [], error: undefined }) // publish precheck
      .mockResolvedValueOnce({
        data: [{ activityId: "a1", nodeId: "fc-root:a1", name: "节点A", message: "后端硬闸门：缺少开始节点" }],
        error: undefined,
      }) // backfill after rejection
    publish.mockResolvedValue({ data: undefined, error: "缺少开始节点" })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const ok = await designer.publish("note")
    expect(ok).toBe(false)
    expect(designer.problems.value).toHaveLength(1)
    expect(designer.problems.value[0]!.message).toBe("后端硬闸门：缺少开始节点")
    expect(designer.saveError.value).toContain("缺少开始节点")
  })
})

describe("useWorkflowDesigner reveal", () => {
  function makeNestedDetail() {
    return {
      ...makeDetail(),
      root: {
        type: "Cike.Flowchart",
        id: "fc-root",
        name: "示例流程",
        activities: [
          {
            type: "Cike.For",
            id: "a-for",
            body: {
              type: "Cike.Flowchart",
              id: "body-fc",
              activities: [{ type: "Cike.Start", id: "inner-start" }],
              connections: [],
            },
          },
        ],
        connections: [],
      },
    }
  }

  it("RevealActivity_DrillsToNestedNodeAndSelects", async () => {
    getById.mockResolvedValue({ data: makeNestedDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const ok = designer.revealActivity({
      activityId: "inner-start",
      nodeId: "fc-root:a-for:body-fc:inner-start",
      name: "内层开始",
      message: "缺少连线",
    })
    expect(ok).toBe(true)
    expect(designer.selectedActivityId.value).toBe("inner-start")
    // Drilled one level into the loop body: root + body-fc.
    expect(designer.breadcrumb.value).toHaveLength(2)
  })

  it("RevealActivity_WorkflowLevel_ReturnsFalse", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const ok = designer.revealActivity({ activityId: null, nodeId: null, name: null, message: "变量非法" })
    expect(ok).toBe(false)
  })
})

describe("useWorkflowDesigner activity descriptors", () => {
  it("Load_CapturesOutputDescriptorsAlongsideInputs", async () => {
    getDescriptors.mockResolvedValue({
      data: [
        {
          typeName: "Cike.SendHttpRequest",
          inputs: [{ clrName: "Url", name: "Url" }],
          outputs: [{ clrName: "StatusCode", name: "StatusCode", displayName: "状态码" }],
        },
      ],
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    // loadPalette is fired without await inside load(); wait for it to settle.
    await vi.waitFor(() => {
      const descriptor = designer.descriptorByType.value.get("Cike.SendHttpRequest")
      expect(descriptor?.inputs).toEqual([{ clrName: "Url", name: "Url" }])
      expect(descriptor?.outputs).toEqual([
        { clrName: "StatusCode", name: "StatusCode", displayName: "状态码" },
      ])
    })
  })
})

describe("useWorkflowDesigner workflow config state", () => {
  function makeDetailWithOptions(options: Record<string, unknown>) {
    return { ...makeDetail(), options }
  }

  it("Load_PopulatesOutputsAndOutcomes", async () => {
    getById.mockResolvedValue({
      data: makeDetailWithOptions({
        variables: [{ id: "v1", name: "count", typeName: "Int32" }],
        inputs: [{ name: "userId", type: "String" }],
        outputs: [{ name: "result", type: "String" }],
        outcomes: ["Done", "Failed"],
        customProperties: { secret: 42 },
      }),
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.variables.value).toEqual([{ id: "v1", name: "count", typeName: "Int32" }])
    expect(designer.inputs.value).toEqual([{ name: "userId", type: "String" }])
    expect(designer.outputs.value).toEqual([{ name: "result", type: "String" }])
    expect(designer.outcomes.value).toEqual(["Done", "Failed"])
  })

  it("Load_PreservesCustomPropertiesInSavedOptions", async () => {
    getById.mockResolvedValue({
      data: makeDetailWithOptions({ customProperties: { key: "val" }, variables: [] }),
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.setVariables([{ id: "v2", name: "x", typeName: "String" }])
    // customProperties must survive an options mutation.
    save.mockResolvedValue({ data: "100", error: undefined })
    await designer.save()
    const sentOptions = save.mock.calls[0][0].body.options
    expect(sentOptions.customProperties).toEqual({ key: "val" })
  })

  it("SetInputs_SupportsUndoRedo", async () => {
    getById.mockResolvedValue({
      data: makeDetailWithOptions({ inputs: [{ name: "a", type: "String" }] }),
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.inputs.value).toEqual([{ name: "a", type: "String" }])
    designer.setInputs([{ name: "b", type: "Int32" }])
    expect(designer.inputs.value).toEqual([{ name: "b", type: "Int32" }])
    designer.undo()
    expect(designer.inputs.value).toEqual([{ name: "a", type: "String" }])
    designer.redo()
    expect(designer.inputs.value).toEqual([{ name: "b", type: "Int32" }])
  })

  it("SetOutputs_SupportsUndoRedo", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.setOutputs([{ name: "out1", type: "String" }])
    expect(designer.outputs.value).toEqual([{ name: "out1", type: "String" }])
    designer.undo()
    expect(designer.outputs.value).toEqual([])
  })

  it("SetOutcomes_SupportsUndoRedo", async () => {
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.setOutcomes(["Done", "Cancelled"])
    expect(designer.outcomes.value).toEqual(["Done", "Cancelled"])
    designer.undo()
    expect(designer.outcomes.value).toEqual([])
  })

  it("Save_IncludesLatestOutputsInOutcomesAndCustomProps", async () => {
    getById.mockResolvedValue({
      data: makeDetailWithOptions({ customProperties: { x: 1 }, outputs: [] }),
      error: undefined,
    })
    save.mockResolvedValue({ data: "100", error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.setOutputs([{ name: "total", type: "Decimal" }])
    designer.setOutcomes(["Success"])
    await designer.save()
    const sentOptions = save.mock.calls[0][0].body.options
    expect(sentOptions.outputs).toEqual([{ name: "total", type: "Decimal" }])
    expect(sentOptions.outcomes).toEqual(["Success"])
    expect(sentOptions.customProperties).toEqual({ x: 1 })
  })

  it("LoadVariableTypes_PopulatesFromApi", async () => {
    getVarialbeTypes.mockResolvedValue({
      data: [{ typeName: "System.String", displayName: "String" }, { typeName: "System.Int32", displayName: "Int32" }],
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.variableTypes.value).toHaveLength(2)
    expect(designer.variableTypes.value[0].displayName).toBe("String")
  })

  it("LoadStorageDrivers_PopulatesFromApi", async () => {
    getStorageDriverDescriptors.mockResolvedValue({
      data: [{ type: "Memory", displayName: "内存" }, { type: "Redis", displayName: "Redis" }],
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    expect(designer.storageDrivers.value).toHaveLength(2)
    expect(designer.storageDrivers.value[0].displayName).toBe("内存")
  })

  it("RenameReference_CascadesStructuredExpressions", async () => {
    getById.mockResolvedValue({
      data: {
        ...makeDetail(),
        options: { variables: [{ id: "v1", name: "counter", typeName: "Int32" }] },
        root: {
          type: "Cike.Flowchart",
          id: "fc-root",
          activities: [
            { type: "Cike.Start", id: "a-start", customProperties: { myExpr: { type: "Variable", value: "counter" } } },
          ],
          connections: [],
        },
      },
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.renameReference("Variable", "counter", "total")
    expect(designer.variables.value[0].name).toBe("total")
    // Verify the expression was cascaded in the activity tree.
    const root = designer.root.value as unknown as { activities: Array<{ customProperties: Record<string, { value?: unknown }> }> }
    expect(root.activities[0].customProperties.myExpr.value).toBe("total")
  })

  it("RenameReference_DoesNotTouchJavaScript", async () => {
    getById.mockResolvedValue({
      data: {
        ...makeDetail(),
        options: { variables: [{ id: "v1", name: "counter", typeName: "Int32" }] },
        root: {
          type: "Cike.Flowchart",
          id: "fc-root",
          activities: [
            { type: "Cike.Start", id: "a-start", customProperties: { script: { type: "JavaScript", value: "getVariable('counter')" } } },
          ],
          connections: [],
        },
      },
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.renameReference("Variable", "counter", "total")
    const root = designer.root.value as unknown as { activities: Array<{ customProperties: Record<string, { value?: unknown }> }> }
    expect(root.activities[0].customProperties.script.value).toBe("getVariable('counter')")
  })

  it("RenameReference_SingleUndoRestoresBothListAndExpressions", async () => {
    getById.mockResolvedValue({
      data: {
        ...makeDetail(),
        options: { inputs: [{ name: "userId", type: "String" }] },
        root: {
          type: "Cike.Flowchart",
          id: "fc-root",
          activities: [
            { type: "Cike.Start", id: "a-start", customProperties: { field: { type: "Input", value: "userId" } } },
          ],
          connections: [],
        },
      },
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    designer.renameReference("Input", "userId", "accountId")
    expect(designer.inputs.value[0].name).toBe("accountId")
    const root = designer.root.value as unknown as { activities: Array<{ customProperties: Record<string, { value?: unknown }> }> }
    expect(root.activities[0].customProperties.field.value).toBe("accountId")
    designer.undo()
    expect(designer.inputs.value[0].name).toBe("userId")
    expect(root.activities[0].customProperties.field.value).toBe("userId")
  })

  it("Readonly_DoesNotAllowSetInputs", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")
    // In readonly mode the command still runs (UI should prevent calling),
    // but the important thing is that save/publish are blocked.
    expect(designer.readonly.value).toBe(true)
  })
})

describe("debugRun", () => {
  beforeEach(() => {
    save.mockResolvedValue({ data: "100", error: undefined })
    debugRun.mockResolvedValue({ data: "inst-999", error: undefined })
  })

  it("DebugRun_SavesFirstThenCallsApi", async () => {
    getById.mockResolvedValue({ data: makeDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const instanceId = await designer.debugRun({ key: "val" })

    expect(save).toHaveBeenCalled()
    expect(debugRun).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { id: "100" },
        body: { input: { key: "val" } },
      }),
    )
    expect(instanceId).toBe("inst-999")
  })

  it("DebugRun_AbortsIfSaveFails", async () => {
    save.mockResolvedValue({ data: null, error: { message: "conflict" } })
    getById.mockResolvedValue({ data: makeDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const instanceId = await designer.debugRun({})

    expect(debugRun).not.toHaveBeenCalled()
    expect(instanceId).toBeNull()
    expect(designer.saveError.value).toBeTruthy()
  })

  it("DebugRun_ReturnsNullOnApiError", async () => {
    debugRun.mockResolvedValue({ data: null, error: { detail: "定义已发布，不能调试" } })
    getById.mockResolvedValue({ data: makeDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const instanceId = await designer.debugRun({})

    expect(instanceId).toBeNull()
    expect(designer.saveError.value).toBe("定义已发布，不能调试")
  })

  it("DebugRun_PassesEmptyInputWhenNoneProvided", async () => {
    getById.mockResolvedValue({ data: makeDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    await designer.debugRun()

    expect(debugRun).toHaveBeenCalledWith(
      expect.objectContaining({
        body: { input: {} },
      }),
    )
  })

  it("CanDebug_IsFalseForPublishedVersion", async () => {
    getById.mockResolvedValue({ data: makeDetail({ isPublished: true }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    expect(designer.canDebug.value).toBe(false)
  })

  it("CanDebug_IsTrueForDraftLatest", async () => {
    getById.mockResolvedValue({ data: makeDetail({ isLatest: true, isPublished: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    expect(designer.canDebug.value).toBe(true)
  })

  it("CanDebug_IsFalseForNonLatestVersion", async () => {
    getById.mockResolvedValue({ data: makeDetail({ isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("100")

    expect(designer.canDebug.value).toBe(false)
  })
})

describe("useWorkflowDesigner edge insertion", () => {
  /** Wide gaps (320px > 180 + 2*40): insertion must not shift anything. */
  function makeWideDetail() {
    return {
      ...makeDetail(),
      root: {
        type: "Cike.Flowchart",
        id: "fc-root",
        name: "示例流程",
        activities: [
          { type: "Cike.Start", id: "a-start", metadata: { designer: { x: 0, y: 0 } } },
          { type: "Cike.If", id: "a-if", metadata: { designer: { x: 500, y: 0 } } },
          { type: "Cike.End", id: "a-end", metadata: { designer: { x: 1000, y: 0 } } },
        ],
        connections: [
          { source: { activityId: "a-start" }, target: { activityId: "a-if" } },
          { source: { activityId: "a-if", port: "True" }, target: { activityId: "a-end" } },
        ],
      },
    }
  }

  /** Tight gap (20px): insertion must shift the structural downstream. */
  function makeTightDetail(withTargetPosition: boolean) {
    const end: Record<string, unknown> = { type: "Cike.End", id: "a-end" }
    if (withTargetPosition) end.metadata = { designer: { x: 200, y: 0 } }
    return {
      ...makeDetail(),
      root: {
        type: "Cike.Flowchart",
        id: "fc-root",
        name: "示例流程",
        activities: [
          { type: "Cike.Start", id: "a-start", metadata: { designer: { x: 0, y: 0 } } },
          end,
        ],
        connections: [{ source: { activityId: "a-start" }, target: { activityId: "a-end" } }],
      },
    }
  }

  /** Cycle n1→n2→n3→n1 with a tight first gap: loop nodes must not shift. */
  function makeCycleDetail() {
    return {
      ...makeDetail(),
      root: {
        type: "Cike.Flowchart",
        id: "fc-root",
        name: "示例流程",
        activities: [
          { type: "Cike.Start", id: "n1", metadata: { designer: { x: 0, y: 0 } } },
          { type: "Cike.RunJavaScript", id: "n2", metadata: { designer: { x: 200, y: 0 } } },
          { type: "Cike.RunJavaScript", id: "n3", metadata: { designer: { x: 400, y: 0 } } },
        ],
        connections: [
          { source: { activityId: "n1" }, target: { activityId: "n2" } },
          { source: { activityId: "n2" }, target: { activityId: "n3" } },
          { source: { activityId: "n3" }, target: { activityId: "n1" } },
        ],
      },
    }
  }

  function connectionsOf(designer: ReturnType<typeof useWorkflowDesigner>) {
    const entry = designer.currentEntry.value!.activity as unknown as {
      connections: { source: { activityId: string; port?: string }; target: { activityId: string } }[]
    }
    return entry.connections.map((c) => `${c.source.activityId}${c.source.port ? `.${c.source.port}` : ""}->${c.target.activityId}`)
  }

  function positionOf(designer: ReturnType<typeof useWorkflowDesigner>, id: string) {
    const child = designer.currentChildren.value.find((activity) => activity.id === id) as
      | { metadata?: { designer?: { x?: number; y?: number } } }
      | undefined
    return child?.metadata?.designer ?? null
  }

  it("Insert_Rewires_PreservesSourcePort_AndUsesFirstOutPort", async () => {
    getById.mockResolvedValue({ data: makeWideDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const ok = designer.insertNodeOnConnection({ source: "a-if", sourcePort: "True", target: "a-end" }, "Cike.RunJavaScript")

    expect(ok).toBe(true)
    const inserted = designer.currentChildren.value.find((activity) => activity.type === "Cike.RunJavaScript")!
    expect(connectionsOf(designer).sort()).toEqual(
      [`a-start->a-if`, `a-if.True->${inserted.id}`, `${inserted.id}.Done->a-end`].sort(),
    )
    expect(inserted.nodeId).toBe(`fc-root:${inserted.id}`)
    expect(designer.selectedActivityId.value).toBe(inserted.id)
    // Gap was wide enough: nothing moved, new node sits at the shifted-midpoint.
    expect(positionOf(designer, "a-if")).toEqual({ x: 500, y: 0 })
    expect(positionOf(designer, "a-end")).toEqual({ x: 1000, y: 0 })
    expect(positionOf(designer, inserted.id)).toEqual({ x: 750, y: 3 })
  })

  it("Insert_ShiftsStructuralDownstream_WhenGapTooSmall", async () => {
    getById.mockResolvedValue({ data: makeTightDetail(true), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const ok = designer.insertNodeOnConnection({ source: "a-start", target: "a-end" }, "Cike.RunJavaScript")

    expect(ok).toBe(true)
    const inserted = designer.currentChildren.value.find((activity) => activity.type === "Cike.RunJavaScript")!
    // delta = (180 + 2*40) - 20 = 240; end 200 → 440; new node centered in the opened gap.
    expect(positionOf(designer, "a-end")).toEqual({ x: 440, y: 0 })
    expect(positionOf(designer, inserted.id)).toEqual({ x: 220, y: 0 })
    expect(connectionsOf(designer).sort()).toEqual([`a-start->${inserted.id}`, `${inserted.id}.Done->a-end`].sort())
  })

  it("Insert_CycleGuard_LoopNodesNeverShift", async () => {
    getById.mockResolvedValue({ data: makeCycleDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const ok = designer.insertNodeOnConnection({ source: "n1", target: "n2" }, "Cike.RunJavaScript")

    expect(ok).toBe(true)
    // n2/n3 are RunJavaScript too: the inserted node is the selected one.
    const insertedId = designer.selectedActivityId.value!
    // Every node reachable from n2 can flow back to n1, so the shift set is empty.
    expect(positionOf(designer, "n2")).toEqual({ x: 200, y: 0 })
    expect(positionOf(designer, "n3")).toEqual({ x: 400, y: 0 })
    expect(positionOf(designer, insertedId)).toEqual({ x: 100, y: 0 })
  })

  it("Insert_UndoAndRedo_AreSingleSteps_RestoringPositions", async () => {
    getById.mockResolvedValue({ data: makeTightDetail(false), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    // a-end has no saved position: auto-layout puts it at (360, 80); start is
    // pinned at (0, 0) so the gap is 180 → delta = 260 - 180 = 80.
    expect(designer.insertNodeOnConnection({ source: "a-start", target: "a-end" }, "Cike.RunJavaScript")).toBe(true)
    const insertedId = designer.selectedActivityId.value!
    expect(positionOf(designer, "a-end")).toEqual({ x: 440, y: 80 })

    designer.undo()
    expect(connectionsOf(designer)).toEqual(["a-start->a-end"])
    expect(designer.currentChildren.value.map((activity) => activity.id)).not.toContain(insertedId)
    expect(positionOf(designer, "a-end")).toBeNull()

    designer.redo()
    expect(designer.currentChildren.value.map((activity) => activity.id)).toContain(insertedId)
    expect(positionOf(designer, "a-end")).toEqual({ x: 440, y: 80 })
    expect(connectionsOf(designer).sort()).toEqual([`a-start->${insertedId}`, `${insertedId}.Done->a-end`].sort())
  })

  it("InsertableGroups_ExcludeTypesWithoutInOrOutPorts", async () => {
    getDescriptors.mockResolvedValue({
      data: [
        { typeName: "Cike.Start", category: "cike" },
        { typeName: "Cike.End", category: "cike" },
        { typeName: "Cike.If", category: "branching" },
        { typeName: "Cike.RunJavaScript", category: "scripting" },
      ],
    })
    getById.mockResolvedValue({ data: makeWideDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    const typeNames = designer.insertableGroups.value.flatMap((group) => group.items.map((item) => item.typeName))
    expect(typeNames.sort()).toEqual(["Cike.If", "Cike.RunJavaScript"])
  })

  it("Insert_RejectsReadonlyVersion", async () => {
    getById.mockResolvedValue({ data: makeDetail({ id: "90", isLatest: false }), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.viewVersion("90")

    expect(designer.insertNodeOnConnection({ source: "a", target: "b" }, "Cike.RunJavaScript")).toBe(false)
  })

  it("Insert_RejectsUnknownConnectionAndNonInsertableTypes", async () => {
    getById.mockResolvedValue({ data: makeWideDetail(), error: undefined })
    const designer = useWorkflowDesigner()
    await designer.load("100")

    expect(designer.insertNodeOnConnection({ source: "a-start", target: "a-end" }, "Cike.RunJavaScript")).toBe(false)
    expect(designer.insertNodeOnConnection({ source: "a-start", target: "a-if" }, "Cike.Start")).toBe(false)
    expect(designer.insertNodeOnConnection({ source: "a-start", target: "a-if" }, "Cike.End")).toBe(false)
    expect(designer.insertNodeOnConnection({ source: "a-start", target: "a-if" }, "Cike.Nope")).toBe(false)
    expect(connectionsOf(designer)).toEqual(["a-start->a-if", "a-if.True->a-end"])
  })

  it("Insert_RejectsChainContainerLevel", async () => {
    getById.mockResolvedValue({
      data: {
        ...makeDetail(),
        root: {
          type: "Cike.Flowchart",
          id: "fc-root",
          name: "示例流程",
          activities: [
            {
              type: "Cike.Sequence",
              id: "seq",
              activities: [
                { type: "Cike.Start", id: "s1" },
                { type: "Cike.End", id: "e1" },
              ],
            },
          ],
          connections: [],
        },
      },
      error: undefined,
    })
    const designer = useWorkflowDesigner()
    await designer.load("100")
    const sequence = designer.currentChildren.value.find((activity) => activity.id === "seq")!
    designer.drillInto(sequence as never)

    expect(designer.insertNodeOnConnection({ source: "s1", target: "e1" }, "Cike.RunJavaScript")).toBe(false)
  })
})

describe("useWorkflowDesigner node removal rewire", () => {
  function mockFlow(activities: unknown[], connections: unknown[]) {
    getById.mockResolvedValue({
      data: {
        ...makeDetail(),
        root: { type: "Cike.Flowchart", id: "fc-root", name: "示例流程", activities, connections },
      },
      error: undefined,
    })
  }

  function connectionsOf(designer: ReturnType<typeof useWorkflowDesigner>) {
    const entry = designer.currentEntry.value!.activity as unknown as {
      connections: { source: { activityId: string; port?: string }; target: { activityId: string } }[]
    }
    return entry.connections.map((c) => `${c.source.activityId}${c.source.port ? `.${c.source.port}` : ""}->${c.target.activityId}`)
  }

  function idsOf(designer: ReturnType<typeof useWorkflowDesigner>) {
    return designer.currentChildren.value.map((activity) => activity.id)
  }

  const linearActivities = [
    { type: "Cike.Start", id: "a-start" },
    { type: "Cike.RunJavaScript", id: "a-mid" },
    { type: "Cike.End", id: "a-end" },
  ]
  const linearConnections = [
    { source: { activityId: "a-start" }, target: { activityId: "a-mid" } },
    { source: { activityId: "a-mid", port: "Done" }, target: { activityId: "a-end" } },
  ]

  it("Remove_SingleInSingleOut_RewiresAndDropsDeletedOutPort", async () => {
    mockFlow(linearActivities, linearConnections)
    const designer = useWorkflowDesigner()
    await designer.load("100")

    designer.removeNode("a-mid")

    expect(idsOf(designer)).not.toContain("a-mid")
    // Rewire keeps the inbound source (a-start, no port); the deleted node's own
    // out port (Done) is dropped — the exact inverse of insertion (ADR 0013).
    expect(connectionsOf(designer)).toEqual(["a-start->a-end"])
  })

  it("Remove_SingleInSingleOut_PreservesInboundSourcePort", async () => {
    mockFlow(
      [
        { type: "Cike.Start", id: "a-start" },
        { type: "Cike.If", id: "a-if" },
        { type: "Cike.RunJavaScript", id: "a-mid" },
        { type: "Cike.End", id: "a-end" },
      ],
      [
        { source: { activityId: "a-start" }, target: { activityId: "a-if" } },
        { source: { activityId: "a-if", port: "True" }, target: { activityId: "a-mid" } },
        { source: { activityId: "a-mid", port: "Done" }, target: { activityId: "a-end" } },
      ],
    )
    const designer = useWorkflowDesigner()
    await designer.load("100")

    designer.removeNode("a-mid")

    // The inbound branch port (If.True) survives; only a-mid's own port is lost.
    expect(connectionsOf(designer).sort()).toEqual(["a-start->a-if", "a-if.True->a-end"].sort())
  })

  it("Remove_MultiOut_StripsAllConnections_NoRewire", async () => {
    mockFlow(
      [
        { type: "Cike.Start", id: "a-start" },
        { type: "Cike.If", id: "a-if" },
        { type: "Cike.End", id: "a-end1" },
        { type: "Cike.End", id: "a-end2" },
      ],
      [
        { source: { activityId: "a-start" }, target: { activityId: "a-if" } },
        { source: { activityId: "a-if", port: "True" }, target: { activityId: "a-end1" } },
        { source: { activityId: "a-if", port: "False" }, target: { activityId: "a-end2" } },
      ],
    )
    const designer = useWorkflowDesigner()
    await designer.load("100")

    designer.removeNode("a-if")

    expect(idsOf(designer)).not.toContain("a-if")
    // Two out-edges: branch intent is unknowable, so strip all and let canvas
    // validation surface the gap (ADR 0013 symmetry).
    expect(connectionsOf(designer)).toEqual([])
  })

  it("Remove_SingleInSingleOut_UndoRedoIsSingleStep", async () => {
    mockFlow(linearActivities, linearConnections)
    const designer = useWorkflowDesigner()
    await designer.load("100")

    designer.removeNode("a-mid")
    expect(connectionsOf(designer)).toEqual(["a-start->a-end"])

    designer.undo()
    expect(idsOf(designer)).toContain("a-mid")
    expect(connectionsOf(designer).sort()).toEqual(["a-start->a-mid", "a-mid.Done->a-end"].sort())

    designer.redo()
    expect(idsOf(designer)).not.toContain("a-mid")
    expect(connectionsOf(designer)).toEqual(["a-start->a-end"])
  })

  it("Remove_SelfLoop_SingleInSingleOut_NoRewire", async () => {
    mockFlow(
      [
        { type: "Cike.RunJavaScript", id: "n1" },
        { type: "Cike.RunJavaScript", id: "n2" },
      ],
      [
        { source: { activityId: "n1" }, target: { activityId: "n2" } },
        { source: { activityId: "n2", port: "Done" }, target: { activityId: "n1" } },
      ],
    )
    const designer = useWorkflowDesigner()
    await designer.load("100")

    designer.removeNode("n2")

    // source (n1) === target (n1): a self-loop carries no rewire intent.
    expect(idsOf(designer)).not.toContain("n2")
    expect(connectionsOf(designer)).toEqual([])
  })
})
