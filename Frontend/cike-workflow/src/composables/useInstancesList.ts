import { computed, ref, type Ref } from "vue"
import {
  postApiV1WorkflowInstancesPagedList,
  postApiV1WorkflowInstancesCancelById,
  getApiV1WorkflowDefinitionsOptionList,
} from "@/api/generated"
import type {
  WorkflowInstanceItemDto,
  WorkflowInstanceFilter,
  WorkflowDefinitionOptionDto,
  WorkflowStatus,
} from "@/api/generated"
import { extractApiErrorMessage } from "@/lib/apiError"

/**
 * Seam: the workflow-instance list composable. Owns every decision the list page
 * makes — workspace-scoped paged fetching, the three orthogonal filter dimensions
 * plus the two toggles, pagination, and empty-state discrimination.
 * InstancesView.vue is a thin template bound to what this exposes; component-local
 * UI state is intentionally not modelled here (project tests run in node, no DOM).
 *
 * ADR-0012 guardrail: this module NEVER populates `workflowMainStatuses` on the
 * filter — status filtering goes exclusively through `workflowStatuses`.
 */

/** Pending / Executing / Suspended — the "Running" main-status members. */
export const RUNNING_STATUSES: readonly WorkflowStatus[] = [0, 1, 2]

/** Only running instances can be cancelled; terminal states are no-ops server-side. */
export function canCancel(status: WorkflowStatus | undefined | null): boolean {
  return status != null && (RUNNING_STATUSES as readonly number[]).includes(status)
}

export function useInstancesList(workspaceId: string) {
  // --- filter state (three orthogonal dimensions + two toggles) ---
  const searchTerm = ref("")
  const selectedStatuses: Ref<WorkflowStatus[]> = ref([])
  const selectedDefinitionIds: Ref<string[]> = ref([])
  const hasIncidents = ref(false)
  const isDebug = ref(false)
  const definitionOptions: Ref<WorkflowDefinitionOptionDto[]> = ref([])

  // --- pagination ---
  const page = ref(1)
  const pageSize = ref(20)
  const total = ref(0)

  // --- data & load state ---
  const items: Ref<WorkflowInstanceItemDto[]> = ref([])
  const loading = ref(false)
  const loadError = ref<string | null>(null)
  const actionError = ref<string | null>(null)
  const cancellingIds: Ref<Set<string>> = ref(new Set())

  const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

  const hasActiveFilters = computed(
    () =>
      searchTerm.value.trim() !== "" ||
      selectedStatuses.value.length > 0 ||
      selectedDefinitionIds.value.length > 0 ||
      hasIncidents.value ||
      isDebug.value,
  )

  // Empty-state discrimination: "no instances at all" vs "nothing matches filters".
  const isEmpty = computed(
    () => !loading.value && !loadError.value && total.value === 0 && !hasActiveFilters.value,
  )
  const isFilteredEmpty = computed(
    () => !loading.value && !loadError.value && total.value === 0 && hasActiveFilters.value,
  )

  /** Assemble the filter body. Never sets workflowMainStatuses (ADR-0012). */
  function buildFilter(): WorkflowInstanceFilter {
    // isDebug is always sent explicitly: backend `??= false` makes omitting
    // equivalent to false, but sending it keeps the debug/production view explicit.
    const filter: WorkflowInstanceFilter = { workspaceId, isDebug: isDebug.value }
    const term = searchTerm.value.trim()
    if (term) filter.searchTerm = term
    if (selectedStatuses.value.length > 0) filter.workflowStatuses = [...selectedStatuses.value]
    if (selectedDefinitionIds.value.length > 0) filter.definitionIds = [...selectedDefinitionIds.value]
    if (hasIncidents.value) filter.hasIncidents = true
    return filter
  }

  // Monotonic sequence guard: only the latest in-flight fetch may write state, so
  // overlapping requests (rapid filter/page changes) can't be clobbered by a stale
  // response, and a response arriving after unmount becomes a harmless no-op.
  let fetchSeq = 0

  async function fetch(): Promise<void> {
    const seq = ++fetchSeq
    loading.value = true
    loadError.value = null
    const { data, error } = await postApiV1WorkflowInstancesPagedList({
      query: { Page: page.value, PageSize: pageSize.value },
      body: buildFilter(),
    })
    if (seq !== fetchSeq) return // a newer request superseded this one — discard stale response
    if (error) {
      loadError.value = extractApiErrorMessage(error, "无法获取实例列表，请检查后端服务后重试")
      items.value = []
      total.value = 0
      loading.value = false
      return
    }
    items.value = (data?.items ?? []) as WorkflowInstanceItemDto[]
    total.value = Number(data?.total ?? 0)
    // If the result set shrank (e.g. rows cancelled elsewhere), the current page may
    // now be out of range — clamp to the last page and refetch so we never show a
    // blank table while total > 0. Converges (page strictly decreases, floor 1).
    if (page.value > totalPages.value) {
      page.value = totalPages.value
      return fetch()
    }
    loading.value = false
  }

  async function loadDefinitionOptions(): Promise<void> {
    const { data, error } = await getApiV1WorkflowDefinitionsOptionList({ query: { workspaceId } })
    if (error) {
      actionError.value = extractApiErrorMessage(error, "加载工作流定义选项失败")
      definitionOptions.value = []
      return
    }
    definitionOptions.value = data ?? []
  }

  /** Refetch the current page (used by the manual refresh button and post-cancel). */
  function refresh(): Promise<void> {
    return fetch()
  }

  /** Any filter change resets to page 1 then refetches. */
  function applyFilters(): Promise<void> {
    page.value = 1
    return fetch()
  }

  function setPage(next: number): Promise<void> {
    page.value = Math.min(Math.max(1, next), totalPages.value)
    return fetch()
  }

  function setPageSize(next: number): Promise<void> {
    pageSize.value = next
    page.value = 1
    return fetch()
  }

  function toggleStatus(status: WorkflowStatus): void {
    const idx = selectedStatuses.value.indexOf(status)
    if (idx >= 0) selectedStatuses.value.splice(idx, 1)
    else selectedStatuses.value.push(status)
  }

  function setDefinitionFilter(ids: string[]): void {
    selectedDefinitionIds.value = [...ids]
  }

  function clearFilters(): Promise<void> {
    searchTerm.value = ""
    selectedStatuses.value = []
    selectedDefinitionIds.value = []
    hasIncidents.value = false
    isDebug.value = false
    page.value = 1
    return fetch()
  }

  /**
   * Cancel a running instance. Backend cancel is async + idempotent: on success we
   * do NOT optimistically flip the row — we clear the "cancelling" marker and
   * refetch so the real (eventually Cancelled) status comes back from the server.
   */
  async function cancel(id: string): Promise<boolean> {
    actionError.value = null
    cancellingIds.value = new Set(cancellingIds.value).add(id)
    const { error } = await postApiV1WorkflowInstancesCancelById({ path: { id } })
    const next = new Set(cancellingIds.value)
    next.delete(id)
    cancellingIds.value = next
    if (error) {
      actionError.value = extractApiErrorMessage(error, "取消失败，请稍后重试")
      return false
    }
    await fetch()
    return true
  }

  /** Initial load: definition options (for the combobox) + first page. */
  function init(): Promise<void> {
    return Promise.all([loadDefinitionOptions(), fetch()]).then(() => undefined)
  }

  return {
    // filter state
    searchTerm,
    selectedStatuses,
    selectedDefinitionIds,
    hasIncidents,
    isDebug,
    definitionOptions,
    // pagination
    page,
    pageSize,
    total,
    totalPages,
    // data & state
    items,
    loading,
    loadError,
    actionError,
    cancellingIds,
    // derived
    hasActiveFilters,
    isEmpty,
    isFilteredEmpty,
    // actions
    fetch,
    refresh,
    applyFilters,
    setPage,
    setPageSize,
    toggleStatus,
    setDefinitionFilter,
    clearFilters,
    cancel,
    loadDefinitionOptions,
    init,
  }
}
