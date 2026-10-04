import { computed, ref, type Ref } from "vue"
import {
  getApiV1WorkflowInstancesById,
  getApiV1WorkflowDefinitionsById,
} from "@/api/generated"
import type {
  ActivityStatus,
  InputDefinition,
  OutputDefinition,
  WorkflowInstanceDetailDto,
  WorkflowStatus,
} from "@/api/generated"
import { watch as signalrWatch, unwatch as signalrUnwatch, onProgress } from "@/composables/useSignalR"
import {
  buildActivityStatusMap,
  applyProgressEvent,
  getLatestRecord,
  type ExecutionRecordLike,
  type ProgressEvent,
} from "@/core/designer/execution"
import { extractApiErrorMessage } from "@/lib/apiError"

/**
 * Composable managing the real-time execution state for a workflow instance
 * detail page. Handles:
 * - Initial HTTP snapshot load (instance + definition)
 * - SignalR Watch/Unwatch lifecycle
 * - Real-time status map updates from progress events
 * - Selected node execution record resolution
 */

/** Map workflow-level terminal event types to WorkflowStatus enum values. */
const WORKFLOW_EVENT_TO_STATUS: Record<number, WorkflowStatus> = {
  4: 3, // WorkflowFinished → Completed
  5: 2, // WorkflowSuspended → Suspended
  6: 5, // WorkflowFaulted → Faulted
  7: 4, // WorkflowCanceled → Canceled
}

export function useInstanceExecution(instanceId: string) {
  const loading = ref(true)
  const loadError = ref<string | null>(null)
  const instance = ref<WorkflowInstanceDetailDto | null>(null)
  const definitionRoot = ref<unknown>(null)
  /** Declared workflow arguments from the definition version's options (I/O schema). */
  const inputDefs = ref<InputDefinition[]>([])
  const outputDefs = ref<OutputDefinition[]>([])
  const statusMap: Ref<Map<string, ActivityStatus>> = ref(new Map())
  const instanceStatus = ref<WorkflowStatus | null>(null)
  const selectedActivityId = ref<string | null>(null)
  const activityRecords = ref<ExecutionRecordLike[]>([])

  let unsubscribe: (() => void) | null = null

  const selectedRecord = computed(() => {
    if (!selectedActivityId.value) return null
    return getLatestRecord(activityRecords.value, selectedActivityId.value) ?? null
  })

  function handleProgressEvent(event: ProgressEvent): void {
    // Activity-level events (type 0..3) update the status map
    if (event.type < 4) {
      statusMap.value = applyProgressEvent(statusMap.value, event)
    } else {
      // Workflow-level terminal events update the header status
      const newStatus = WORKFLOW_EVENT_TO_STATUS[event.type]
      if (newStatus != null) instanceStatus.value = newStatus
    }
  }

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null

    try {
      const { data, error } = await getApiV1WorkflowInstancesById({ path: { id: instanceId } })
      if (error || !data) {
        loadError.value = extractApiErrorMessage(error, "加载实例失败")
        return
      }

      instance.value = data
      instanceStatus.value = data.status ?? null
      activityRecords.value = (data.activityInstances ?? []) as ExecutionRecordLike[]
      statusMap.value = buildActivityStatusMap(activityRecords.value)

      // Load definition for canvas projection
      if (data.definitionVersionId) {
        const def = await getApiV1WorkflowDefinitionsById({ path: { id: data.definitionVersionId } })
        if (def.error || !def.data) {
          loadError.value = extractApiErrorMessage(def.error, "加载定义版本失败")
          return
        }
        definitionRoot.value = (def.data as Record<string, unknown>).root ?? null
        const options = (def.data as { options?: { inputs?: InputDefinition[]; outputs?: OutputDefinition[] } | null }).options
        inputDefs.value = options?.inputs ?? []
        outputDefs.value = options?.outputs ?? []
      }

      // Subscribe to real-time events
      unsubscribe = onProgress(handleProgressEvent)
      await signalrWatch(instanceId)
    } finally {
      loading.value = false
    }
  }

  function dispose(): void {
    unsubscribe?.()
    unsubscribe = null
    signalrUnwatch(instanceId)
  }

  return {
    loading,
    loadError,
    instance,
    definitionRoot,
    inputDefs,
    outputDefs,
    statusMap,
    instanceStatus,
    selectedActivityId,
    selectedRecord,
    activityRecords,
    load,
    dispose,
  }
}
