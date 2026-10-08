import { computed, ref, shallowRef } from "vue"
import {
  getApiV1CommonsActivityDescriptors,
  getApiV1CommonsExpressionDescriptors,
  getApiV1CommonsStorageDriverDescriptors,
  getApiV1CommonsVarialbeTypes,
  getApiV1WorkflowDefinitionsById,
  getApiV1WorkflowDefinitionsVersionList,
  postApiV1WorkflowDefinitionsPublishById,
  postApiV1WorkflowDefinitionsRollback,
  postApiV1WorkflowDefinitionsSaveById,
  postApiV1WorkflowDefinitionsValidateCanvas,
  postApiV1WorkflowInstancesDebugRunById,
} from "@/api/generated"
import type { WorkflowCanvasValidationErrorDto, WorkflowDefinitionType, WorkflowDefinitionVersionItemDto } from "@/api/generated"
import type { Activity, IActivity } from "@/core/abstracts/Activity"
import { Activity as ActivityClass } from "@/core/abstracts/Activity"
import { Flowchart } from "@/core/activities/Flowchart"
import { ActivityConnection } from "@/core/models/ActivityConnection"
import { ActivityEndpoint } from "@/core/models/ActivityEndpoint"
import {
  CommandStack,
  makeAddNodeCommand,
  makeBatchCommand,
  makeConnectCommand,
  makeDisconnectCommand,
  makeMoveNodeCommand,
  makeRemoveNodeCommand,
  makeResizeNodeCommand,
  makeShiftNodesCommand,
  type DesignerCommand,
} from "@/core/designer/commands"
import { buildPaletteGroups, type PaletteGroup } from "@/core/designer/palette"
import type { ExpressionDescriptor, InputDefinition, InputDescriptor, OutputDefinition, OutputDescriptor, StorageDriverDescriptor, VariableDefinition, VariableTypeDescriptor } from "@/api/generated"
import { ensureDrillTarget, isChainContainer } from "@/core/designer/drill"
import { resolveRevealPath } from "@/core/designer/reveal"
import { projectOrderedChain, projectFlowchart, projectCanvas, canDrillInto, getDefaultNodeSize, getOutPortsOf, getInPortsOf, type CanvasProjection } from "@/core/designer/projection"
import { computeInsertPosition, computeShiftDelta, computeShiftIds, findConnectionByRef, type ConnectionRef } from "@/core/designer/insertion"
import { activityShortName, resolveActivityClass } from "@/core/designer/registry"
import { fromWireActivity, toWireActivity, type WireActivity } from "@/core/designer/serialization"
import { computeNodeId } from "@/core/designer/nodeId"
import { getCanvasState, getNodePosition, setCanvasState, type DesignerCanvasMeta, type DesignerNodeMeta } from "@/core/designer/metadata"
import { cascadeRename, type ReferenceKind } from "@/core/designer/rename"
import { extractApiErrorMessage } from "@/lib/apiError"

/** One drill-down level: the container owning the canvas content. */
export interface DrillEntry {
  /** Container whose children are displayed on this level. */
  activity: Activity
  title: string
  /** When set, this level is an ordered chain (no connections in the model). */
  chainChildren?: IActivity[]
}

/**
 * A single canvas validation problem, normalized from the backend
 * `WorkflowCanvasValidationErrorDto`. The backend is the single source of truth
 * for validation rules (ADR 0002); the frontend only renders what it returns.
 */
export interface ValidationProblem {
  /** Offending activity id; null for workflow-level problems (e.g. variables). */
  activityId: string | null
  /** Structural path (ADR 0003) used to drill to the activity across containers. */
  nodeId: string | null
  /** Display name of the offending activity, when the backend provides one. */
  name: string | null
  message: string
}

export function useWorkflowDesigner() {
  const root = shallowRef<Activity | null>(null)
  const definitionName = ref("")
  const loadError = ref<string | null>(null)
  const loading = ref(false)
  /** Business identifier, constant across every version of the definition. */
  const definitionId = ref("")
  /** Metadata of the currently loaded version row, for toolbar display + edit. */
  const description = ref("")
  const definitionType = ref<WorkflowDefinitionType | undefined>(undefined)
  const usableAsActivity = ref(false)
  const materializerName = ref("")
  const folderId = ref("")
  const version = ref(0)
  const isLatest = ref(true)
  const isPublished = ref(false)
  /** Every version row of this definition (newest first as returned by API). */
  const versions = shallowRef<WorkflowDefinitionVersionItemDto[]>([])
  /** Transport error of the last version-list fetch; surfaced in the history sheet. */
  const versionsError = ref<string | null>(null)
  /** Historic (non-latest) versions are frozen: canvas is view-only. */
  const readonly = computed(() => !isLatest.value)
  /** Debug requires a draft latest version (published rows are rejected by backend). */
  const canDebug = computed(() => isLatest.value && !isPublished.value)
  const drillStack = shallowRef<DrillEntry[]>([])
  const selectedActivityId = ref<string | null>(null)
  const rowId = ref<string | null>(null)
  const savedOptions = shallowRef<Record<string, unknown>>({})
  const saving = ref(false)
  const saveError = ref<string | null>(null)
  const lastSavedAt = ref<Date | null>(null)
  const canUndo = ref(false)
  const canRedo = ref(false)
  const commandStack = new CommandStack()
  /** Bumped on every model mutation (command/undo/redo) so model-derived
   *  computeds re-run — the domain model is held in shallowRefs and commands
   *  mutate plain nested arrays that Vue cannot track on its own. */
  const revision = ref(0)
  const paletteGroups = shallowRef<PaletteGroup[]>([])
  /** Wire type → descriptor lookup (inputs drive the generic property form;
   *  outputs drive the node output-binding section). */
  const descriptorByType = shallowRef<Map<string, { inputs?: InputDescriptor[]; outputs?: OutputDescriptor[] }>>(new Map())
  /** Wire type → backend icon name; enriches projected nodes for canvas rendering. */
  const iconByType = shallowRef<Map<string, string>>(new Map())
  /** Options-level workflow variables. */
  const variables = shallowRef<VariableDefinition[]>([])
  /** Options-level workflow inputs; drive the WorkflowInput expression picker. */
  const inputs = shallowRef<InputDefinition[]>([])
  /** Options-level workflow outputs. */
  const outputs = shallowRef<OutputDefinition[]>([])
  /** Options-level workflow outcomes (control-flow results). */
  const outcomes = shallowRef<string[]>([])
  /** Backend-registered variable/argument types for type selectors. */
  const variableTypes = shallowRef<VariableTypeDescriptor[]>([])
  /** Backend-registered storage drivers for storage-driver selectors. */
  const storageDrivers = shallowRef<StorageDriverDescriptor[]>([])
  /** Backend-registered expression types; drive the ExpressionEditor type list. */
  const expressionDescriptors = shallowRef<ExpressionDescriptor[]>([])
  /** Canvas validation problems; the problem list panel is their only outlet. */
  const problems = ref<ValidationProblem[]>([])
  /** True while a ValidateCanvas request is in flight. */
  const validating = ref(false)
  /** Transport failure of the last validation — distinct from a clean canvas. */
  const validationError = ref<string | null>(null)
  /** Monotonic guard so only the latest validation response is applied. */
  let validationSeq = 0
  /** Debounce handle coalescing rapid edits into one validation request. */
  let validateTimer: ReturnType<typeof setTimeout> | null = null

  const currentEntry = computed<DrillEntry | null>(() => drillStack.value[drillStack.value.length - 1] ?? null)

  const breadcrumb = computed(() => drillStack.value.map((entry) => entry.title))

  const projection = computed<CanvasProjection>(() => {
    void revision.value
    const icons = iconByType.value
    // Reading problems here makes the projection reactive to validation results,
    // overlaying error markers by activityId (same pattern as run status).
    const errorIds = new Set(
      problems.value.map((problem) => problem.activityId).filter((id): id is string => id != null),
    )
    const entry = currentEntry.value
    if (!entry) return { nodes: [], edges: [] }
    const base = entry.chainChildren
      ? projectOrderedChain(entry.chainChildren)
      : entry.activity instanceof Flowchart
        ? projectFlowchart(entry.activity)
        : "activities" in entry.activity && Array.isArray((entry.activity as { activities?: unknown }).activities)
          ? projectCanvas(
              entry.activity as unknown as {
                activities: IActivity[]
                connections: import("@/core/models/ActivityConnection").ActivityConnection[]
              },
            )
          : { nodes: [], edges: [] }
    // Enrich with the backend-provided icon (keyed by wire type); pure projection
    // has no descriptor access so icon resolution lives here.
    return {
      edges: base.edges,
      nodes: base.nodes.map((node) => ({
        ...node,
        data: { ...node.data, icon: icons.get(node.data.type) ?? null, hasError: errorIds.has(node.data.activityId) },
      })),
    }
  })

  const currentChildren = computed<IActivity[]>(() => {
    void revision.value
    const entry = currentEntry.value
    if (!entry) return []
    if (entry.chainChildren) return entry.chainChildren
    if (entry.activity instanceof Flowchart) return entry.activity.activities
    const activities = (entry.activity as { activities?: unknown }).activities
    return Array.isArray(activities) ? (activities as IActivity[]) : []
  })

  const selectedActivity = computed<IActivity | null>(() => {
    // Depend on revision so a delete/undo re-runs this: makeRemoveNodeCommand
    // splices the activities array in place (same reference), so a computed that
    // only reads currentChildren would keep the stale cached node.
    void revision.value
    if (!selectedActivityId.value) return null
    return currentChildren.value.find((child) => child.id === selectedActivityId.value) ?? null
  })

  /** Inbound edge count per activity id on the current level (MergeMode UI gate). */
  const connectionTargets = computed(() => {
    const map = new Map<string, number>()
    for (const edge of projection.value.edges) {
      map.set(edge.target, (map.get(edge.target) ?? 0) + 1)
    }
    return map
  })

  // Monotonic sequence guard: rapid version/definition switches fire overlapping load
  // calls; only the latest may write designer state, so an older version's late response
  // can't clobber the one now on screen. (Mirrors the existing validationSeq pattern.)
  let loadSeq = 0

  async function load(definitionRowId: string): Promise<void> {
    const seq = ++loadSeq
    loading.value = true
    loadError.value = null
    // A reload supersedes any in-flight edit validation and clears stale results
    // so a readonly/historic view never shows the previous version's problems.
    if (validateTimer) {
      clearTimeout(validateTimer)
      validateTimer = null
    }
    problems.value = []
    validationError.value = null
    try {
      const { data, error } = await getApiV1WorkflowDefinitionsById({ path: { id: definitionRowId } })
      if (seq !== loadSeq) return // stale — a newer load owns the designer state
      if (error || !data) {
        loadError.value = extractApiErrorMessage(error, "加载定义失败")
        return
      }
      rowId.value = definitionRowId
      savedOptions.value = (data.options ?? {}) as Record<string, unknown>
      variables.value = ((data.options as { variables?: VariableDefinition[] } | null)?.variables ?? []) as VariableDefinition[]
      inputs.value = ((data.options as { inputs?: InputDefinition[] } | null)?.inputs ?? []) as InputDefinition[]
      outputs.value = ((data.options as { outputs?: OutputDefinition[] } | null)?.outputs ?? []) as OutputDefinition[]
      outcomes.value = ((data.options as { outcomes?: string[] } | null)?.outcomes ?? []) as string[]
      definitionName.value = data.name || data.definitionId || "未命名"
      definitionId.value = data.definitionId ?? ""
      description.value = data.description ?? ""
      definitionType.value = data.type
      usableAsActivity.value = data.usableAsActivity ?? false
      materializerName.value = data.materializerName ?? ""
      folderId.value = data.folderId ?? ""
      version.value = data.version ?? 0
      isLatest.value = data.isLatest ?? true
      isPublished.value = data.isPublished ?? false
      root.value = fromWireActivity((data.root ?? {}) as WireActivity)
      drillStack.value = [{ activity: root.value, title: definitionName.value }]
      selectedActivityId.value = null
      selectedEdgeId.value = null
      commandStack.clear()
      refreshUndoFlags()
      void loadPalette()
      void loadExpressionDescriptors()
      void loadVariableTypes()
      void loadStorageDrivers()
      void loadVersions()
      // Validate once on open (non-blocking) so a loaded draft's existing
      // problems surface immediately. No-op in readonly mode.
      void validate()
    } finally {
      if (seq === loadSeq) loading.value = false
    }
  }

  function drillInto(activity: IActivity): void {
    if (!canDrillInto(activity)) return
    if (!(activity instanceof ActivityClass)) return
    const target = ensureDrillTarget(activity)
    if (!target) return
    const title = activity.name ?? activityShortName(activity.type)
    const entry: DrillEntry = isChainContainer(target)
      ? { activity: target, title, chainChildren: (target as unknown as { activities: IActivity[] }).activities }
      : { activity: target, title }
    drillStack.value = [...drillStack.value, entry]
    selectedActivityId.value = null
  }

  function popTo(index: number): void {
    if (index < 0 || index >= drillStack.value.length) return
    drillStack.value = drillStack.value.slice(0, index + 1)
    selectedActivityId.value = null
  }

  /**
   * Drills to and selects the node a validation problem points at, resolving the
   * location from its NodeId chain (or activityId fallback). Returns false when
   * the problem cannot be located (workflow-level, or a stale node reference) —
   * the problem list renders such rows as non-clickable.
   */
  function revealActivity(problem: ValidationProblem): boolean {
    if (!root.value) return false
    const path = resolveRevealPath(root.value, problem.nodeId, problem.activityId)
    if (!path) return false
    // Reset to the root level, then replay the drill steps to reach the target.
    popTo(0)
    for (const target of path.drillTargets) drillInto(target)
    selectedActivityId.value = path.targetId
    return true
  }

  function refreshUndoFlags(): void {
    canUndo.value = commandStack.canUndo()
    canRedo.value = commandStack.canRedo()
  }

  function executeCommand(command: DesignerCommand): void {
    commandStack.execute(command)
    revision.value++
    refreshUndoFlags()
    scheduleValidation()
  }

  function undo(): void {
    commandStack.undo()
    revision.value++
    refreshUndoFlags()
    scheduleValidation()
  }

  function redo(): void {
    commandStack.redo()
    revision.value++
    refreshUndoFlags()
    scheduleValidation()
  }

  function moveNode(payload: { id: string; x: number; y: number; from: { x: number; y: number } | null }): void {
    const activity = currentChildren.value.find((child) => child.id === payload.id)
    if (!activity) return
    executeCommand(makeMoveNodeCommand(activity as IActivity, payload.from, { x: payload.x, y: payload.y }))
  }

  function resizeNode(payload: { id: string; width: number; height: number; from: { width: number; height: number } | null }): void {
    const activity = currentChildren.value.find((child) => child.id === payload.id)
    if (!activity) return
    executeCommand(makeResizeNodeCommand(activity as IActivity, payload.from, { width: payload.width, height: payload.height }))
  }

  /** Builds the delete command; the UI confirms before executing it. */
  function buildRemoveCommand(activityId: string): DesignerCommand | null {
    const entry = currentEntry.value
    if (!entry) return null
    const container = entry.activity as unknown as { activities: IActivity[]; connections?: unknown[] }
    return makeRemoveNodeCommand(container as never, activityId)
  }

  function removeNode(activityId: string): void {
    const command = buildRemoveCommand(activityId)
    if (!command) return
    executeCommand(command)
    if (selectedActivityId.value === activityId) selectedActivityId.value = null
  }

  function saveViewport(state: DesignerCanvasMeta): void {
    const entry = currentEntry.value
    if (!entry) return
    setCanvasState(entry.activity, state)
  }

  async function loadPalette(): Promise<void> {
    try {
      const { data } = await getApiV1CommonsActivityDescriptors({})
      const descriptors = (data ?? []) as Array<{ typeName?: string; inputs?: InputDescriptor[]; outputs?: OutputDescriptor[]; icon?: string | null }>
      paletteGroups.value = buildPaletteGroups(descriptors)
      const lookup = new Map<string, { inputs?: InputDescriptor[]; outputs?: OutputDescriptor[] }>()
      const icons = new Map<string, string>()
      for (const descriptor of descriptors) {
        if (!descriptor.typeName) continue
        lookup.set(descriptor.typeName, { inputs: descriptor.inputs, outputs: descriptor.outputs })
        if (descriptor.icon) icons.set(descriptor.typeName, descriptor.icon)
      }
      descriptorByType.value = lookup
      iconByType.value = icons
    } catch {
      paletteGroups.value = []
    }
  }

  /** Fetches the backend-registered expression types once (global, not per-definition). */
  async function loadExpressionDescriptors(): Promise<void> {
    if (expressionDescriptors.value.length > 0) return
    try {
      const { data } = await getApiV1CommonsExpressionDescriptors({})
      expressionDescriptors.value = (data ?? []) as ExpressionDescriptor[]
    } catch {
      expressionDescriptors.value = []
    }
  }

  /** Fetches the backend-registered variable/argument types once. */
  async function loadVariableTypes(): Promise<void> {
    if (variableTypes.value.length > 0) return
    try {
      const { data } = await getApiV1CommonsVarialbeTypes({})
      variableTypes.value = (data ?? []) as VariableTypeDescriptor[]
    } catch {
      variableTypes.value = []
    }
  }

  /** Fetches the backend-registered storage drivers once. */
  async function loadStorageDrivers(): Promise<void> {
    if (storageDrivers.value.length > 0) return
    try {
      const { data } = await getApiV1CommonsStorageDriverDescriptors({})
      storageDrivers.value = (data ?? []) as StorageDriverDescriptor[]
    } catch {
      storageDrivers.value = []
    }
  }

  /** Adds a node of the given wire type; point is canvas-local, auto-staggered
   *  down-right while it would overlap an existing node. */
  function addNode(typeName: string, point: { x: number; y: number }): void {
    const Ctor = resolveActivityClass(typeName)
    if (!Ctor) return
    const entry = currentEntry.value
    if (!entry || entry.chainChildren) return
    const activity = new Ctor()
    // NodeId is a structural path identity: derive it O(1) from the parent
    // container's NodeId (ADR 0003), consistent with the backend materializer
    // and with the load-time computation folded into fromWireActivity.
    activity.nodeId = computeNodeId(entry.activity.nodeId, activity.id)
    const occupied = new Set(projection.value.nodes.map((node) => `${Math.round(node.x)},${Math.round(node.y)}`))
    let position = { x: Math.round(point.x), y: Math.round(point.y) }
    while (occupied.has(`${position.x},${position.y}`)) {
      position = { x: position.x + 30, y: position.y + 70 }
    }
    const container = entry.activity as unknown as { activities: IActivity[] }
    executeCommand(makeAddNodeCommand(container as never, activity, position))
    selectedActivityId.value = activity.id
  }

  /** Replaces an options-level array field through the command stack. */
  function setOptionField<T>(key: string, list: T[], localRef: { value: T[] }, label: string): void {
    const from = savedOptions.value[key] as T[] | undefined
    executeCommand({
      label,
      apply: () => {
        savedOptions.value = { ...savedOptions.value, [key]: list }
        localRef.value = list
      },
      undo: () => {
        const restored = { ...savedOptions.value }
        if (from == null) delete restored[key]
        else restored[key] = from
        savedOptions.value = restored
        localRef.value = from ?? []
      },
      redo: () => {
        savedOptions.value = { ...savedOptions.value, [key]: list }
        localRef.value = list
      },
    })
  }

  function setVariables(list: VariableDefinition[]): void {
    setOptionField("variables", list, variables as { value: VariableDefinition[] }, "编辑工作流变量")
  }

  function setInputs(list: InputDefinition[]): void {
    setOptionField("inputs", list, inputs as { value: InputDefinition[] }, "编辑工作流输入")
  }

  function setOutputs(list: OutputDefinition[]): void {
    setOptionField("outputs", list, outputs as { value: OutputDefinition[] }, "编辑工作流输出")
  }

  function setOutcomes(list: string[]): void {
    setOptionField("outcomes", list, outcomes as { value: string[] }, "编辑工作流结果")
  }

  /**
   * Renames a variable or input and cascades the change to all structured
   * Expression references in the activity tree. The rename + cascade is merged
   * into a single undo step (spec decision).
   */
  function renameReference(kind: ReferenceKind, oldName: string, newName: string): void {
    if (!root.value || oldName === newName || !oldName) return
    const list = kind === "Variable" ? variables.value : inputs.value
    const updated = list.map((item) =>
      item.name === oldName ? { ...item, name: newName } : item,
    )
    const fromOptions = savedOptions.value[kind === "Variable" ? "variables" : "inputs"] as typeof list | undefined
    const localRef = kind === "Variable" ? variables : inputs
    const key = kind === "Variable" ? "variables" : "inputs"

    // Snapshot expression values before cascade so undo can restore them.
    const exprSnapshots: Array<{ expr: { value?: unknown }; old: unknown }> = []
    collectExpressions(root.value, kind, oldName, exprSnapshots)

    executeCommand({
      label: `重命名${kind === "Variable" ? "变量" : "输入"} ${oldName}`,
      apply: () => {
        savedOptions.value = { ...savedOptions.value, [key]: updated }
        localRef.value = updated as never
        cascadeRename(root.value!, { kind, oldName, newName })
      },
      undo: () => {
        const restored = { ...savedOptions.value }
        if (fromOptions == null) delete restored[key]
        else restored[key] = fromOptions
        savedOptions.value = restored
        localRef.value = (fromOptions ?? []) as never
        for (const snap of exprSnapshots) snap.expr.value = snap.old
      },
      redo: () => {
        savedOptions.value = { ...savedOptions.value, [key]: updated }
        localRef.value = updated as never
        cascadeRename(root.value!, { kind, oldName, newName })
      },
    })
  }

  /** Collects existing expression values matching the rename target for undo. */
  function collectExpressions(
    activity: IActivity,
    kind: ReferenceKind,
    oldName: string,
    out: Array<{ expr: { value?: unknown }; old: unknown }>,
  ): void {
    const record = activity as unknown as Record<string, unknown>
    for (const key of Object.keys(record)) {
      if (key === "type" || key === "id" || key === "nodeId") continue
      const value = record[key]
      if (isExprLike(value)) {
        if (value.type === kind && value.value === oldName) out.push({ expr: value, old: value.value })
      } else if (Array.isArray(value)) {
        for (const item of value) {
          if (isExprLike(item)) {
            if (item.type === kind && item.value === oldName) out.push({ expr: item, old: item.value })
          } else if (isActivityLike(item)) {
            collectExpressions(item as unknown as IActivity, kind, oldName, out)
          }
        }
      } else if (isActivityLike(value)) {
        collectExpressions(value as unknown as IActivity, kind, oldName, out)
      } else if (value != null && typeof value === "object") {
        collectFromPlain(value as Record<string, unknown>, kind, oldName, out)
      }
    }
  }

  function collectFromPlain(
    obj: Record<string, unknown>,
    kind: ReferenceKind,
    oldName: string,
    out: Array<{ expr: { value?: unknown }; old: unknown }>,
  ): void {
    for (const key of Object.keys(obj)) {
      const value = obj[key]
      if (isExprLike(value)) {
        if (value.type === kind && value.value === oldName) out.push({ expr: value, old: value.value })
      } else if (Array.isArray(value)) {
        for (const item of value) {
          if (isExprLike(item)) {
            if (item.type === kind && item.value === oldName) out.push({ expr: item, old: item.value })
          } else if (isActivityLike(item)) {
            collectExpressions(item as unknown as IActivity, kind, oldName, out)
          } else if (item != null && typeof item === "object") {
            collectFromPlain(item as Record<string, unknown>, kind, oldName, out)
          }
        }
      } else if (isActivityLike(value)) {
        collectExpressions(value as unknown as IActivity, kind, oldName, out)
      } else if (value != null && typeof value === "object") {
        collectFromPlain(value as Record<string, unknown>, kind, oldName, out)
      }
    }
  }

  function isExprLike(v: unknown): v is { type?: string; value?: unknown } {
    return v != null && typeof v === "object" && !Array.isArray(v) && "type" in (v as object) && "value" in (v as object)
  }

  function isActivityLike(v: unknown): boolean {
    return v != null && typeof v === "object" && !Array.isArray(v) && "type" in (v as object) && "id" in (v as object)
  }

  const selectedEdgeId = ref<string | null>(null)

  /** Creates a connection; returns false (rejecting the canvas edge) on duplicates. */
  function connect(payload: { source: string; sourcePort?: string; target: string }): boolean {
    const entry = currentEntry.value
    if (!entry || entry.chainChildren) return false
    if (payload.source === payload.target) return false
    const container = entry.activity as unknown as { connections?: ActivityConnection[] }
    const connections = container.connections ?? []
    const duplicate = connections.some(
      (connection) =>
        connection.source.activityId === payload.source &&
        connection.source.port === payload.sourcePort &&
        connection.target.activityId === payload.target,
    )
    if (duplicate) return false
    executeCommand(
      makeConnectCommand(
        container as never,
        new ActivityConnection(new ActivityEndpoint(payload.source, payload.sourcePort), new ActivityEndpoint(payload.target)),
      ),
    )
    return true
  }

  function removeEdge(edgeId: string): void {
    const entry = currentEntry.value
    if (!entry) return
    const edge = projection.value.edges.find((candidate) => candidate.id === edgeId)
    if (!edge || edge.visual) return
    const container = entry.activity as unknown as { connections?: ActivityConnection[] }
    const connections = container.connections ?? []
    const connection = connections.find(
      (candidate) =>
        candidate.source.activityId === edge.source &&
        (candidate.source.port ?? undefined) === (edge.sourcePort ?? undefined) &&
        candidate.target.activityId === edge.target,
    )
    if (!connection) return
    executeCommand(makeDisconnectCommand(container as never, connection))
    if (selectedEdgeId.value === edgeId) selectedEdgeId.value = null
  }

  function getViewport(): DesignerCanvasMeta | null {
    const entry = currentEntry.value
    if (!entry) return null
    return getCanvasState(entry.activity)
  }

  /** Whether a type can be inserted into a connection: it must have both an
   *  entry and an outcome port (Start/End drop out). */
  function isInsertableType(typeName: string): boolean {
    const Ctor = resolveActivityClass(typeName)
    if (!Ctor) return false
    const probe = new Ctor()
    return getInPortsOf(probe).length > 0 && getOutPortsOf(probe).length > 0
  }

  /** Palette groups filtered to connection-insertable types (insert menu). */
  const insertableGroups = computed<PaletteGroup[]>(() =>
    paletteGroups.value
      .map((group) => ({ ...group, items: group.items.filter((item) => isInsertableType(item.typeName)) }))
      .filter((group) => group.items.length > 0),
  )

  /**
   * Edge insertion (ADR 0013): replace the connection identified by its
   * endpoint triple with source→N and N(first outcome)→target, opening room
   * by shifting the structural downstream only when the gap is too tight.
   * The whole edit is ONE undo step; the new node ends up selected.
   */
  function insertNodeOnConnection(payload: ConnectionRef, typeName: string): boolean {
    const entry = currentEntry.value
    if (!entry || entry.chainChildren || readonly.value) return false
    if (!isInsertableType(typeName)) return false
    const Ctor = resolveActivityClass(typeName)!
    const container = entry.activity as unknown as { activities: IActivity[]; connections?: ActivityConnection[] }
    const connections = container.connections ?? []
    const old = findConnectionByRef(connections, payload)
    if (!old) return false
    const rects = new Map(projection.value.nodes.map((node) => [node.id, node]))
    const sourceRect = rects.get(payload.source)
    const targetRect = rects.get(payload.target)
    if (!sourceRect || !targetRect) return false

    const activity = new Ctor()
    activity.nodeId = computeNodeId(entry.activity.nodeId, activity.id)
    const size = getDefaultNodeSize(activity)
    const delta = computeShiftDelta(sourceRect, targetRect, size.width)
    const shiftIds = delta > 0 ? computeShiftIds(connections, old, payload.source, payload.target) : []
    const moves = shiftIds
      .map((id) => {
        const rect = rects.get(id)
        const shifted = container.activities.find((child) => child.id === id)
        if (!rect || !shifted) return null
        return { activity: shifted, from: getNodePosition(shifted), to: { x: rect.x + delta, y: rect.y } }
      })
      .filter((move): move is { activity: IActivity; from: DesignerNodeMeta | null; to: DesignerNodeMeta } => move !== null)
    // When nothing can shift (cycle guard), the gap stays as-is: place mid-current.
    const appliedDelta = moves.length > 0 ? delta : 0
    const position = computeInsertPosition(sourceRect, targetRect, appliedDelta, size)

    const commands: DesignerCommand[] = [
      makeDisconnectCommand(container as never, old),
      makeAddNodeCommand(container as never, activity, position),
      makeConnectCommand(
        container as never,
        new ActivityConnection(new ActivityEndpoint(payload.source, payload.sourcePort), new ActivityEndpoint(activity.id)),
      ),
      makeConnectCommand(
        container as never,
        new ActivityConnection(new ActivityEndpoint(activity.id, getOutPortsOf(activity)[0]), new ActivityEndpoint(payload.target)),
      ),
    ]
    if (moves.length > 0) commands.push(makeShiftNodesCommand(moves))
    executeCommand(makeBatchCommand("插入节点", commands))
    selectedActivityId.value = activity.id
    selectedEdgeId.value = null
    return true
  }

  /** Debounce window coalescing rapid edits into a single validation request. */
  const VALIDATE_DEBOUNCE_MS = 500

  /**
   * Runs backend canvas validation — the single source of truth for rules
   * (ADR 0002) — and stores the structured problems. Read-only: it never creates
   * a version or mutates the draft. A monotonic sequence guard drops stale
   * responses so a slow request cannot overwrite a newer result.
   */
  async function validate(): Promise<ValidationProblem[]> {
    if (!root.value || readonly.value) return problems.value
    const seq = ++validationSeq
    validating.value = true
    try {
      const { data, error } = await postApiV1WorkflowDefinitionsValidateCanvas({
        body: {
          root: toWireActivity(root.value) as never,
          options: savedOptions.value as never,
        },
      })
      if (seq !== validationSeq) return problems.value
      if (error) {
        validationError.value = extractApiErrorMessage(error, "校验请求失败，请稍后重试")
        return problems.value
      }
      validationError.value = null
      const list = (data ?? []) as WorkflowCanvasValidationErrorDto[]
      problems.value = list.map((item) => ({
        activityId: item.activityId ?? null,
        nodeId: item.nodeId ?? null,
        name: item.name ?? null,
        message: item.message ?? "",
      }))
      return problems.value
    } catch (err) {
      if (seq !== validationSeq) return problems.value
      validationError.value = String(err)
      return problems.value
    } finally {
      if (seq === validationSeq) validating.value = false
    }
  }

  /**
   * Schedules a debounced re-validation after a designer edit. No-op in readonly
   * mode (historic versions and instance traces are never validated).
   */
  function scheduleValidation(): void {
    if (readonly.value) return
    if (validateTimer) clearTimeout(validateTimer)
    validateTimer = setTimeout(() => {
      validateTimer = null
      void validate()
    }, VALIDATE_DEBOUNCE_MS)
  }

  async function save(): Promise<void> {
    if (!root.value || rowId.value == null || saving.value || readonly.value) return
    saving.value = true
    saveError.value = null
    try {
      const { data, error } = await postApiV1WorkflowDefinitionsSaveById({
        path: { id: rowId.value },
        body: {
          root: toWireActivity(root.value) as never,
          options: savedOptions.value as never,
        },
      })
      if (error) {
        saveError.value = extractApiErrorMessage(error, "保存失败，请稍后重试")
        return
      }
      // A published version is immutable: the backend forks a new draft version
      // and returns its id. When it differs from the current row, reload the
      // whole canvas onto the new draft so further edits land there.
      const newRowId = data != null ? String(data) : null
      if (newRowId && newRowId !== rowId.value) {
        await load(newRowId)
      }
      lastSavedAt.value = new Date()
    } catch (err) {
      saveError.value = String(err)
    } finally {
      saving.value = false
    }
  }

  /**
   * Auto-save then dispatch a debug run against the current draft version.
   * Returns the new instance id on success, or null if save/API failed.
   */
  async function debugRun(input?: Record<string, unknown>): Promise<string | null> {
    if (!rowId.value) return null
    // Auto-save first: debug runs against the persisted draft (ADR 0011)
    saveError.value = null
    await save()
    if (saveError.value) return null

    const { data, error } = await postApiV1WorkflowInstancesDebugRunById({
      path: { id: rowId.value },
      body: { input: input ?? {} },
    })
    if (error || data == null) {
      saveError.value = extractApiErrorMessage(error, "调试启动失败，请稍后重试")
      return null
    }
    return String(data)
  }

  async function loadVersions(): Promise<void> {
    if (!definitionId.value) return
    versionsError.value = null
    const { data, error } = await getApiV1WorkflowDefinitionsVersionList({
      query: { definitionId: definitionId.value },
    })
    if (error) {
      versionsError.value = extractApiErrorMessage(error, "加载版本列表失败，请稍后重试")
      versions.value = []
      return
    }
    versions.value = (data ?? []) as WorkflowDefinitionVersionItemDto[]
  }

  /** Loads a version row for view-only inspection (read-only when non-latest). */
  async function viewVersion(versionRowId: string): Promise<void> {
    await load(versionRowId)
  }

  /** Returns from a historic version back to the editable latest version. */
  async function returnToLatest(): Promise<void> {
    // Always refetch: after a rollback the latest version row has changed.
    await loadVersions()
    const latest = versions.value.find((entry) => entry.isLatest)
    if (latest?.id) await load(String(latest.id))
  }

  /**
   * Publishes the current canvas (root + options) with an optional note.
   * Validates first as a pre-publish gate: if the canvas has problems it does not
   * publish and returns false (the UI reveals them from `problems`). Returns true
   * only when the backend accepted the publish.
   */
  async function publish(publishedNote?: string): Promise<boolean> {
    if (!root.value || rowId.value == null || saving.value || readonly.value) return false
    // Pre-publish gate — same rules the backend enforces on publish (ADR 0002).
    const found = await validate()
    if (found.length > 0) return false
    saving.value = true
    saveError.value = null
    try {
      const { data, error } = await postApiV1WorkflowDefinitionsPublishById({
        path: { id: rowId.value },
        body: {
          root: toWireActivity(root.value) as never,
          options: savedOptions.value as never,
          publishedNote: publishedNote ?? null,
        },
      })
      if (error) {
        saveError.value = extractApiErrorMessage(error, "发布失败，请稍后重试")
        // Backend hard gate rejected: re-validate to backfill structured problems
        // so the designer shows which nodes failed, not just a flattened message.
        await validate()
        return false
      }
      // Reload to refresh version state (published flag / new version row).
      const publishedRowId = data != null ? String(data) : rowId.value
      await load(publishedRowId)
      return true
    } catch (err) {
      saveError.value = String(err)
      await validate()
      return false
    } finally {
      saving.value = false
    }
  }

  /** Rolls a historic version back to become the new latest, then reloads. */
  async function rollback(definitionVersionId: string): Promise<void> {
    if (!definitionId.value) return
    const { error } = await postApiV1WorkflowDefinitionsRollback({
      body: { definitionId: definitionId.value, definitionVersionId },
    })
    if (error) {
      saveError.value = extractApiErrorMessage(error, "回滚失败，请稍后重试")
      return
    }
    await returnToLatest()
  }

  return {
    root,
    definitionName,
    definitionId,
    rowId,
    description,
    definitionType,
    usableAsActivity,
    materializerName,
    folderId,
    version,
    isLatest,
    isPublished,
    readonly,
    versions,
    versionsError,
    loadError,
    loading,
    drillStack,
    selectedActivityId,
    selectedActivity,
    currentEntry,
    currentChildren,
    breadcrumb,
    projection,
    problems,
    validating,
    validationError,
    validate,
    saving,
    saveError,
    lastSavedAt,
    canUndo,
    canRedo,
    revision,
    paletteGroups,
    descriptorByType,
    connectionTargets,
    variables,
    inputs,
    outputs,
    outcomes,
    variableTypes,
    storageDrivers,
    expressionDescriptors,
    setVariables,
    setInputs,
    setOutputs,
    setOutcomes,
    renameReference,
    addNode,
    selectedEdgeId,
    connect,
    removeEdge,
    insertNodeOnConnection,
    insertableGroups,
    load,
    loadVersions,
    viewVersion,
    returnToLatest,
    publish,
    rollback,
    drillInto,
    popTo,
    revealActivity,
    executeCommand,
    buildRemoveCommand,
    removeNode,
    undo,
    redo,
    moveNode,
    resizeNode,
    saveViewport,
    getViewport,
    save,
    canDebug,
    debugRun,
  }
}

export type WorkflowDesignerState = ReturnType<typeof useWorkflowDesigner>
