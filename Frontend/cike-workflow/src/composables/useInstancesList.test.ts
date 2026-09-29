import { beforeEach, describe, expect, it, vi } from "vitest"

/**
 * Seam: the instance-list composable. Mocks the generated API client and asserts
 * externally observable behavior — the request sent to PagedList, exposed refs,
 * which calls fire — never internal implementation details.
 *
 * This file grows with the tickets: T2 covers workspace-scoped fetching,
 * pagination and base load/empty states.
 */

const mockPagedList = vi.fn()
const mockOptionList = vi.fn()
const mockCancel = vi.fn()

vi.mock("@/api/generated", () => ({
  postApiV1WorkflowInstancesPagedList: (...args: unknown[]) => mockPagedList(...args),
  getApiV1WorkflowDefinitionsOptionList: (...args: unknown[]) => mockOptionList(...args),
  postApiV1WorkflowInstancesCancelById: (...args: unknown[]) => mockCancel(...args),
}))

import { canCancel, useInstancesList, RUNNING_STATUSES } from "@/composables/useInstancesList"

const WS = "42"

function pagedOk(items: unknown[] = [], total = 0) {
  return { data: { items, total: String(total) }, error: null }
}

describe("useInstancesList — fetch & scope (T2)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
  })

  it("sends workspaceId scope, explicit isDebug=false, and pagination query on fetch", async () => {
    const list = useInstancesList(WS)
    await list.fetch()

    expect(mockPagedList).toHaveBeenCalledTimes(1)
    const call = mockPagedList.mock.calls[0]![0]
    expect(call.query).toEqual({ Page: 1, PageSize: 20 })
    expect(call.body.workspaceId).toBe(WS)
    expect(call.body.isDebug).toBe(false)
  })

  it("populates items and total (total arrives as a string) from the response", async () => {
    const rows = [{ id: "1" }, { id: "2" }]
    mockPagedList.mockResolvedValue(pagedOk(rows, 2))
    const list = useInstancesList(WS)
    await list.fetch()

    expect(list.items.value).toEqual(rows)
    expect(list.total.value).toBe(2)
  })

  it("clears loading after a successful fetch", async () => {
    const list = useInstancesList(WS)
    await list.fetch()
    expect(list.loading.value).toBe(false)
    expect(list.loadError.value).toBeNull()
  })

  it("sets loadError and empties items when the request errors", async () => {
    mockPagedList.mockResolvedValue({ data: null, error: { detail: "boom" } })
    const list = useInstancesList(WS)
    await list.fetch()

    expect(list.loadError.value).toBe("boom")
    expect(list.items.value).toEqual([])
    expect(list.total.value).toBe(0)
    expect(list.loading.value).toBe(false)
  })
})

describe("useInstancesList — pagination (T2)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
  })

  it("computes totalPages as ceil(total/pageSize), at least 1", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 45))
    const list = useInstancesList(WS)
    await list.fetch()
    expect(list.totalPages.value).toBe(3) // ceil(45/20)

    mockPagedList.mockResolvedValue(pagedOk([], 0))
    await list.fetch()
    expect(list.totalPages.value).toBe(1)
  })

  it("setPage refetches with the new page number", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 100))
    const list = useInstancesList(WS)
    await list.fetch()
    await list.setPage(3)

    expect(list.page.value).toBe(3)
    const lastCall = mockPagedList.mock.calls.at(-1)![0]
    expect(lastCall.query.Page).toBe(3)
  })

  it("setPage clamps to the [1, totalPages] range", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 45)) // totalPages = 3
    const list = useInstancesList(WS)
    await list.fetch()

    await list.setPage(99)
    expect(list.page.value).toBe(3)

    await list.setPage(0)
    expect(list.page.value).toBe(1)
  })

  it("setPageSize resets to page 1 and refetches with the new size", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 100))
    const list = useInstancesList(WS)
    await list.fetch()
    await list.setPage(2)
    await list.setPageSize(50)

    expect(list.pageSize.value).toBe(50)
    expect(list.page.value).toBe(1)
    const lastCall = mockPagedList.mock.calls.at(-1)![0]
    expect(lastCall.query).toEqual({ Page: 1, PageSize: 50 })
  })

  it("refresh refetches the current page without changing it", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 100))
    const list = useInstancesList(WS)
    await list.fetch()
    await list.setPage(2)
    mockPagedList.mockClear()

    await list.refresh()
    expect(list.page.value).toBe(2)
    expect(mockPagedList.mock.calls.at(-1)![0].query.Page).toBe(2)
  })
})

describe("useInstancesList — empty state (T2)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
  })

  it("isEmpty is true when there are no rows and no error", async () => {
    const list = useInstancesList(WS)
    await list.fetch()
    expect(list.isEmpty.value).toBe(true)
  })

  it("isEmpty is false when rows exist", async () => {
    mockPagedList.mockResolvedValue(pagedOk([{ id: "1" }], 1))
    const list = useInstancesList(WS)
    await list.fetch()
    expect(list.isEmpty.value).toBe(false)
  })

  it("isEmpty is false when the load errored", async () => {
    mockPagedList.mockResolvedValue({ data: null, error: { detail: "boom" } })
    const list = useInstancesList(WS)
    await list.fetch()
    expect(list.isEmpty.value).toBe(false)
    expect(list.loadError.value).toBeTruthy()
  })
})

describe("useInstancesList — filters (T3)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
    mockOptionList.mockResolvedValue({ data: [{ definitionId: "WF_1", name: "月度审批" }], error: null })
  })

  it("assembles every active filter dimension into the request body", async () => {
    const list = useInstancesList(WS)
    list.searchTerm.value = "  报销  "
    list.toggleStatus(5) // Faulted
    list.toggleStatus(2) // Suspended
    list.setDefinitionFilter(["WF_1"])
    list.hasIncidents.value = true
    list.isDebug.value = true
    await list.applyFilters()

    const body = mockPagedList.mock.calls.at(-1)![0].body
    expect(body.workspaceId).toBe(WS)
    expect(body.searchTerm).toBe("报销") // trimmed
    expect(body.workflowStatuses).toEqual([5, 2])
    expect(body.definitionIds).toEqual(["WF_1"])
    expect(body.hasIncidents).toBe(true)
    expect(body.isDebug).toBe(true)
  })

  it("ADR-0012 guardrail: never sends workflowMainStatuses", async () => {
    const list = useInstancesList(WS)
    list.toggleStatus(1)
    list.hasIncidents.value = true
    await list.applyFilters()
    const body = mockPagedList.mock.calls.at(-1)![0].body
    expect(body).not.toHaveProperty("workflowMainStatuses")
    expect(body).not.toHaveProperty("workflowMainStatus")
  })

  it("omits empty searchTerm and empty selections from the body", async () => {
    const list = useInstancesList(WS)
    await list.applyFilters()
    const body = mockPagedList.mock.calls.at(-1)![0].body
    expect(body.searchTerm).toBeUndefined()
    expect(body.workflowStatuses).toBeUndefined()
    expect(body.definitionIds).toBeUndefined()
    expect(body.hasIncidents).toBeUndefined()
    expect(body.isDebug).toBe(false)
  })

  it("toggleStatus adds then removes a status", () => {
    const list = useInstancesList(WS)
    list.toggleStatus(3)
    expect(list.selectedStatuses.value).toEqual([3])
    list.toggleStatus(3)
    expect(list.selectedStatuses.value).toEqual([])
  })

  it("applyFilters resets to page 1", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 100))
    const list = useInstancesList(WS)
    await list.fetch()
    await list.setPage(3)
    expect(list.page.value).toBe(3)
    await list.applyFilters()
    expect(list.page.value).toBe(1)
  })

  it("hasActiveFilters reflects any non-default filter", async () => {
    const list = useInstancesList(WS)
    expect(list.hasActiveFilters.value).toBe(false)
    list.searchTerm.value = "x"
    expect(list.hasActiveFilters.value).toBe(true)
    list.searchTerm.value = ""
    list.isDebug.value = true
    expect(list.hasActiveFilters.value).toBe(true)
  })

  it("distinguishes isEmpty (no filters) from isFilteredEmpty (filters on)", async () => {
    const list = useInstancesList(WS)
    await list.fetch() // total 0, no filters
    expect(list.isEmpty.value).toBe(true)
    expect(list.isFilteredEmpty.value).toBe(false)

    list.searchTerm.value = "nothing-matches"
    await list.applyFilters() // total 0, filters active
    expect(list.isEmpty.value).toBe(false)
    expect(list.isFilteredEmpty.value).toBe(true)
  })

  it("clearFilters resets every dimension and refetches from page 1", async () => {
    mockPagedList.mockResolvedValue(pagedOk([], 100))
    const list = useInstancesList(WS)
    list.searchTerm.value = "abc"
    list.toggleStatus(5)
    list.setDefinitionFilter(["WF_1"])
    list.hasIncidents.value = true
    list.isDebug.value = true
    await list.applyFilters()
    await list.setPage(2)

    await list.clearFilters()
    expect(list.searchTerm.value).toBe("")
    expect(list.selectedStatuses.value).toEqual([])
    expect(list.selectedDefinitionIds.value).toEqual([])
    expect(list.hasIncidents.value).toBe(false)
    expect(list.isDebug.value).toBe(false)
    expect(list.page.value).toBe(1)
    const body = mockPagedList.mock.calls.at(-1)![0].body
    expect(body.searchTerm).toBeUndefined()
    expect(body.isDebug).toBe(false)
  })

  it("loadDefinitionOptions fetches the workspace option list", async () => {
    const list = useInstancesList(WS)
    await list.loadDefinitionOptions()
    expect(mockOptionList).toHaveBeenCalledWith({ query: { workspaceId: WS } })
    expect(list.definitionOptions.value).toEqual([{ definitionId: "WF_1", name: "月度审批" }])
  })
})

describe("canCancel (T4)", () => {
  it("is true only for Running main-status members", () => {
    expect(RUNNING_STATUSES).toEqual([0, 1, 2])
    expect(canCancel(0)).toBe(true)
    expect(canCancel(1)).toBe(true)
    expect(canCancel(2)).toBe(true)
  })

  it("is false for terminal statuses and nullish", () => {
    expect(canCancel(3)).toBe(false)
    expect(canCancel(4)).toBe(false)
    expect(canCancel(5)).toBe(false)
    expect(canCancel(6)).toBe(false)
    expect(canCancel(undefined)).toBe(false)
    expect(canCancel(null)).toBe(false)
  })
})

describe("useInstancesList — cancel (T4)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
    mockCancel.mockResolvedValue({ error: null })
  })

  it("cancel success clears the cancelling marker and refetches the page", async () => {
    const list = useInstancesList(WS)
    await list.fetch()
    mockPagedList.mockClear()

    const ok = await list.cancel("inst-1")
    expect(ok).toBe(true)
    expect(mockCancel).toHaveBeenCalledWith({ path: { id: "inst-1" } })
    expect(list.cancellingIds.value.has("inst-1")).toBe(false)
    expect(mockPagedList).toHaveBeenCalledTimes(1) // refetched, no optimistic flip
    expect(list.actionError.value).toBeNull()
  })

  it("cancel failure sets actionError and does not refetch", async () => {
    mockCancel.mockResolvedValue({ error: { detail: "实例不存在" } })
    const list = useInstancesList(WS)
    await list.fetch()
    mockPagedList.mockClear()

    const ok = await list.cancel("bad")
    expect(ok).toBe(false)
    expect(list.actionError.value).toBe("实例不存在")
    expect(list.cancellingIds.value.has("bad")).toBe(false)
    expect(mockPagedList).not.toHaveBeenCalled()
  })
})

describe("useInstancesList — fetch lifecycle (review W1/W2)", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPagedList.mockResolvedValue(pagedOk([], 0))
    mockOptionList.mockResolvedValue({ data: [], error: null })
  })

  it("discards a stale response when a newer fetch is in flight (W1)", async () => {
    let resolveFirst: (v: unknown) => void = () => {}
    const first = new Promise<unknown>((r) => {
      resolveFirst = r
    })
    mockPagedList
      .mockReset()
      .mockReturnValueOnce(first) // 1st call hangs
      .mockResolvedValueOnce(pagedOk([{ id: "new" }], 1)) // 2nd resolves first

    const list = useInstancesList(WS)
    const p1 = list.fetch() // seq 1
    const p2 = list.fetch() // seq 2
    await p2
    resolveFirst({ data: { items: [{ id: "old" }], total: "1" }, error: null })
    await p1

    // The late (stale) first response must not clobber the newer result.
    expect(list.items.value).toEqual([{ id: "new" }])
  })

  it("clamps to the last page and refetches when total shrinks below the current page (W2)", async () => {
    const list = useInstancesList(WS)
    mockPagedList.mockResolvedValue(pagedOk([{ id: "x" }], 100)) // totalPages 5
    await list.fetch()
    list.page.value = 5 // simulate sitting on page 5

    mockPagedList.mockResolvedValue(pagedOk([], 40)) // shrink → totalPages 2
    await list.refresh()

    expect(list.page.value).toBe(2)
    expect(mockPagedList.mock.calls.at(-1)![0].query.Page).toBe(2)
  })
})
