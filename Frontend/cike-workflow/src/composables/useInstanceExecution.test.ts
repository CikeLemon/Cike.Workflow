import { beforeEach, describe, expect, it, vi } from "vitest"

/**
 * Seam: the instance execution composable. Tests mock the SignalR layer and
 * the generated API client, asserting externally observable behavior (exposed
 * refs, which API calls fire) — never internal implementation details.
 */

const mockWatch = vi.fn()
const mockUnwatch = vi.fn()
const mockOnProgress = vi.fn()

vi.mock("@/composables/useSignalR", () => ({
  watch: (...args: unknown[]) => mockWatch(...args),
  unwatch: (...args: unknown[]) => mockUnwatch(...args),
  onProgress: (...args: unknown[]) => mockOnProgress(...args),
}))

const mockGetInstance = vi.fn()
const mockGetDefinition = vi.fn()
const mockGetLogs = vi.fn()

vi.mock("@/api/generated", () => ({
  getApiV1WorkflowInstancesById: (...args: unknown[]) => mockGetInstance(...args),
  getApiV1WorkflowDefinitionsById: (...args: unknown[]) => mockGetDefinition(...args),
  getApiV1WorkflowInstancesLogsById: (...args: unknown[]) => mockGetLogs(...args),
}))

import { useInstanceExecution } from "@/composables/useInstanceExecution"
import type { ProgressEvent } from "@/core/designer/execution"

function makeInstanceDto(overrides: Record<string, unknown> = {}) {
  return {
    id: "inst-1",
    name: "测试实例",
    status: 1,
    definitionVersionId: "ver-100",
    definitionName: "月度审批",
    version: 3,
    correlationId: "corr-abc",
    createdAt: "2026-01-01T00:00:00Z",
    activityInstances: [
      { activityId: "a-start", activityNodeId: "fc-root:a-start", status: 2, createdAt: "2026-01-01T00:00:01Z" },
      { activityId: "a-http", activityNodeId: "fc-root:a-http", status: 1, createdAt: "2026-01-01T00:00:02Z" },
    ],
    workflowState: { input: { key: "val" }, output: {} },
    ...overrides,
  }
}

function makeDefinitionDto() {
  return {
    id: "ver-100",
    root: { type: "Cike.Flowchart", id: "fc-root", name: "Root", activities: [], connections: [] },
    options: {},
  }
}

describe("useInstanceExecution", () => {
  let progressCallback: ((event: ProgressEvent) => void) | null = null

  beforeEach(() => {
    vi.clearAllMocks()
    progressCallback = null
    mockOnProgress.mockImplementation((cb: (event: ProgressEvent) => void) => {
      progressCallback = cb
      return () => { progressCallback = null }
    })
    mockGetInstance.mockResolvedValue({ data: makeInstanceDto(), error: null })
    mockGetDefinition.mockResolvedValue({ data: makeDefinitionDto(), error: null })
    mockGetLogs.mockResolvedValue({ data: [], error: null })
  })

  it("loads instance and builds initial statusMap", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    expect(exec.statusMap.value.get("a-start")).toBe(2)
    expect(exec.statusMap.value.get("a-http")).toBe(1)
    expect(exec.instanceStatus.value).toBe(1)
    expect(exec.loading.value).toBe(false)
  })

  it("calls watch on load and unwatch on dispose", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    expect(mockWatch).toHaveBeenCalledWith("inst-1")

    exec.dispose()
    expect(mockUnwatch).toHaveBeenCalledWith("inst-1")
  })

  it("applies real-time progress events to statusMap", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    // Simulate ActivityCompleted for a-http
    progressCallback?.({ type: 1, activityNodeId: "fc-root:a-http" })

    expect(exec.statusMap.value.get("a-http")).toBe(2)
  })

  it("updates instanceStatus on workflow terminal events", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    // WorkflowFinished = type 4
    progressCallback?.({ type: 4, activityNodeId: null })

    expect(exec.instanceStatus.value).toBe(3) // Completed
  })

  it("maps WorkflowFaulted to status 5", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    progressCallback?.({ type: 6, activityNodeId: null })

    expect(exec.instanceStatus.value).toBe(5) // Faulted
  })

  it("maps WorkflowCanceled to status 4", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    progressCallback?.({ type: 7, activityNodeId: null })

    expect(exec.instanceStatus.value).toBe(4) // Canceled
  })

  it("maps WorkflowSuspended to status 2", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    progressCallback?.({ type: 5, activityNodeId: null })

    expect(exec.instanceStatus.value).toBe(2) // Suspended
  })

  it("selectedRecord returns latest record for selected activityId", async () => {
    mockGetInstance.mockResolvedValue({
      data: makeInstanceDto({
        activityInstances: [
          { activityId: "a-http", status: 2, createdAt: "2026-01-01T00:00:01Z", outputs: { r: 1 } },
          { activityId: "a-http", status: 4, createdAt: "2026-01-01T00:00:03Z", outputs: { r: 2 } },
        ],
      }),
      error: null,
    })

    const exec = useInstanceExecution("inst-1")
    await exec.load()
    exec.selectedActivityId.value = "a-http"

    expect(exec.selectedRecord.value?.status).toBe(4)
    expect(exec.selectedRecord.value?.outputs).toEqual({ r: 2 })
  })

  it("selectedRecord is null when no activity selected", async () => {
    const exec = useInstanceExecution("inst-1")
    await exec.load()

    expect(exec.selectedRecord.value).toBeNull()
  })

  it("sets loadError on API failure", async () => {
    mockGetInstance.mockResolvedValue({ data: null, error: { message: "fail" } })

    const exec = useInstanceExecution("inst-1")
    await exec.load()

    expect(exec.loadError.value).toBeTruthy()
    expect(exec.loading.value).toBe(false)
  })
})
