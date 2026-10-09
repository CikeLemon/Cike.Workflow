<template>
  <div ref="containerRef" class="canvas-surface h-full w-full bg-muted/20" @dragover.prevent @drop="onDrop" />
  <component :is="TeleportContainer" v-if="TeleportContainer" />
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from "vue"
import { Graph, Snapline, Transform, type Node as X6Node } from "@antv/x6"
import type { CanvasProjection } from "@/core/designer/projection"
import type { ConnectionRef } from "@/core/designer/insertion"
import { getCanvasState, type DesignerCanvasMeta } from "@/core/designer/metadata"
import { CIKE_NODE_SHAPE, registerDesignerShapes, TeleportContainer } from "./nodes/register"

const props = defineProps<{
  projection: CanvasProjection
  interactive: boolean
  selectedId: string | null
  /** Identity of the current drill level; changing it restores its viewport. */
  entryKey: string
  entryActivity: unknown
  /** Edge whose insert menu is open: its button stays visible while pinned.
   *  Optional — read-only canvases (instance debug) never pass it. */
  insertMenuEdgeId?: string | null
  /** Selected edge: drives the highlight from parent state so it survives
   *  full projection re-renders. Optional — instance debug never selects. */
  selectedEdgeId?: string | null
}>()

const emit = defineEmits<{
  nodeClick: [activityId: string]
  nodeDblclick: [activityId: string]
  nodeMoved: [payload: { id: string; x: number; y: number; from: { x: number; y: number } | null }]
  nodeResized: [payload: { id: string; width: number; height: number; from: { width: number; height: number } | null }]
  viewportChanged: [state: DesignerCanvasMeta]
  dropActivity: [payload: { typeName: string; x: number; y: number }]
  edgeClick: [edgeId: string]
  connectRequest: [payload: { edgeId: string; source: string; sourcePort?: string; target: string }]
  insertRequest: [payload: ConnectionRef & { edgeId: string; x: number; y: number }]
}>()

const containerRef = ref<HTMLDivElement>()
let graph: Graph | null = null
/** Positions captured on node:mousedown for move-command undo. */
const dragOrigins = new Map<string, { x: number; y: number } | null>()
/** Sizes captured on node:resize:start for resize-command undo. */
const resizeOrigins = new Map<string, { width: number; height: number } | null>()

function onDrop(event: DragEvent): void {
  const typeName = event.dataTransfer?.getData("cike-activity-type")
  if (!typeName || !graph) return
  const point = graph.clientToLocal(event.clientX, event.clientY)
  emit("dropActivity", { typeName, x: point.x, y: point.y })
}

/** Selection look for exactly one edge: primary color (the CSS class flips
 *  the currentColor the stroke inherits) plus a thicker line. Idempotent, so
 *  it can be re-applied after every full projection re-render. */
function paintEdgeSelection(edgeId: string | null): void {
  if (!graph) return
  for (const edge of graph.getEdges()) {
    const selected = String(edge.id) === edgeId
    graph.findViewByCell(edge)?.container.classList.toggle("edge-selected", selected)
    edge.setAttrByPath("line/strokeWidth", selected ? 2.5 : 1.5)
  }
}

function selectEdge(edgeId: string): void {
  if (props.selectedEdgeId === edgeId) return
  // Paint now for instant feedback; the selectedEdgeId watch reconciles.
  paintEdgeSelection(edgeId)
  emit("edgeClick", edgeId)
}

/** Edge-insert button (ADR 0013): a hover affordance at the edge midpoint.
 *  Registered as X6's built-in "button" tool — that name is also its identity
 *  for hasTool/removeTool, and these edges carry no other button tools. */
const INSERT_TOOL_NAME = "button"

interface ToolBearingCell {
  hasTool: (name: string) => boolean
  addTools: (tool: unknown) => void
  removeTool: (name: string) => void
}

function isInsertableEdge(edgeId: string): boolean {
  if (!props.interactive) return false
  const edge = props.projection.edges.find((candidate) => candidate.id === edgeId)
  // Chain-container visual edges carry no model connection to insert into.
  return !!edge && !edge.visual
}

function addInsertTool(edgeId: string): void {
  const cell = graph?.getCellById(edgeId) as ToolBearingCell | null | undefined
  if (!cell || cell.hasTool(INSERT_TOOL_NAME)) return
  cell.addTools({
    name: "button",
    args: {
      distance: 0.5,
      markup: [
        { tagName: "circle", selector: "button", attrs: { r: 10, class: "insert-btn-circle", "data-insert-btn": "" } },
        { tagName: "path", selector: "icon", attrs: { d: "M -4 0 H 4 M 0 -4 V 4", class: "insert-btn-icon" } },
      ],
      onClick: ({ e }: { e: MouseEvent }) => {
        // X6's Button tool already stopPropagation+preventDefault on the
        // mousedown it fires this from, so panning is suppressed for free.
        onInsertButtonClick(e, edgeId)
      },
    },
  })
}

function removeInsertTool(edgeId: string): void {
  const cell = graph?.getCellById(edgeId) as ToolBearingCell | null | undefined
  if (cell?.hasTool(INSERT_TOOL_NAME)) cell.removeTool(INSERT_TOOL_NAME)
}

function onInsertButtonClick(evt: MouseEvent, edgeId: string): void {
  const edge = props.projection.edges.find((candidate) => candidate.id === edgeId)
  if (!edge) return
  emit("insertRequest", {
    edgeId,
    source: edge.source,
    sourcePort: edge.sourcePort,
    target: edge.target,
    x: evt.clientX,
    y: evt.clientY,
  })
}

onMounted(() => {
  registerDesignerShapes()
  graph = new Graph({
    container: containerRef.value!,
    // Synchronous rendering: x6-vue-shape node views must be appended to the
    // SVG immediately; X6's default async renderer leaves them unmounted here.
    async: false,
    autoResize: true,
    grid: false,
    interacting: props.interactive,
    mousewheel: { enabled: true, factor: 1.2, zoomAtMousePosition: true },
    panning: { enabled: true, eventTypes: ["leftMouseDown", "rightMouseDown"] },
    connecting: {
      allowBlank: false,
      allowLoop: false,
      allowNode: false,
      allowEdge: false,
      allowPort: true,
      allowMulti: "withPort",
      highlight: true,
      connectionPoint: "boundary",
      connector: { name: "smooth" },
      validateConnection: ({ sourceCell, targetCell, targetPort }) => {
        if (!sourceCell || !targetCell || sourceCell === targetCell) return false
        const target = props.projection.nodes.find((node) => node.id === String(targetCell.id))
        return !!targetPort && !!target && target.data.inPorts.includes(String(targetPort))
      },
      createEdge: () =>
        graph!.createEdge({
          shape: "edge",
          attrs: { line: { stroke: "currentColor", strokeWidth: 1.5, targetMarker: null } },
        }),
    },
  })
  if (props.interactive) {
    graph.use(new Snapline())
    graph.use(new Transform({ resizing: { enabled: true, minWidth: 120, minHeight: 32 } }))
  }
  graph.on("node:click", ({ node }) => emit("nodeClick", String(node.id)))
  graph.on("blank:click", () => {
    paintEdgeSelection(null)
    emit("nodeClick", "")
  })
  graph.on("edge:click", ({ edge }) => selectEdge(String(edge.id)))
  graph.on("edge:mouseenter", ({ edge }) => {
    if (isInsertableEdge(String(edge.id))) addInsertTool(String(edge.id))
  })
  graph.on("edge:mouseleave", ({ edge }) => {
    // Keep the button while its menu is open; otherwise hover-only.
    if (String(edge.id) !== props.insertMenuEdgeId) removeInsertTool(String(edge.id))
  })
  graph.on("edge:connected", ({ edge, isNew }) => {
    if (!isNew) return
    const source = edge.getSource()
    const target = edge.getTarget()
    emit("connectRequest", {
      edgeId: String(edge.id),
      source: String(typeof source === "object" && "cell" in source ? source.cell : source),
      sourcePort: typeof source === "object" && "port" in source && source.port ? String(source.port) : undefined,
      target: String(typeof target === "object" && "cell" in target ? target.cell : target),
    })
  })
  graph.on("node:dblclick", ({ node }) => emit("nodeDblclick", String(node.id)))
  graph.on("node:mousedown", ({ node }) => {
    const activity = props.entryActivity as { activities?: { id: string; metadata?: { designer?: { x?: number; y?: number } } }[] } | undefined
    const child = activity && Array.isArray(activity.activities)
      ? activity.activities.find((a) => a.id === node.id)
      : undefined
    const designer = child?.metadata?.designer
    const from = designer && typeof designer.x === "number" && typeof designer.y === "number"
      ? { x: designer.x, y: designer.y }
      : null
    dragOrigins.set(String(node.id), from)
  })
  graph.on("node:moved", ({ node }) => {
    const { x, y } = node.getPosition()
    emit("nodeMoved", { id: String(node.id), x, y, from: dragOrigins.get(String(node.id)) ?? null })
  })
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  graph.on("node:resize:start", (args: any) => {
    const node = args.node as X6Node
    const { width, height } = node.getSize()
    resizeOrigins.set(String(node.id), { width, height })
  })
  graph.on("node:resized", ({ node }) => {
    const { width, height } = node.getSize()
    emit("nodeResized", { id: String(node.id), width, height, from: resizeOrigins.get(String(node.id)) ?? null })
  })
  graph.on("scale", ({ sx }) => {
    const translation = graph!.translate()
    emit("viewportChanged", { zoom: sx, panX: translation.tx, panY: translation.ty })
  })
  graph.on("translate", ({ tx, ty }) => {
    emit("viewportChanged", { zoom: graph!.zoom(), panX: tx, panY: ty })
  })
  renderProjection()
  applyViewport()
  // The insert button lives on X6's decorator layer, but its tool container
  // carries data-cell-id, so a click on it would still resolve to the edge and
  // fire edge:click (mis-selecting) or blank:click (deselecting). X6's Button
  // tool already stops the MOUSEDOWN (no pan); we additionally swallow the
  // button's CLICK in the capture phase. We must NOT guard mousedown here: X6
  // fires the tool's onClick from its own mousedown handler, and a capture-phase
  // mousedown guard would stop it before the button ever runs.
  const guardClick = (event: Event): void => {
    if ((event.target as Element).closest?.("[data-insert-btn]")) event.stopPropagation()
  }
  containerRef.value!.addEventListener("click", guardClick, true)
})

watch(() => props.insertMenuEdgeId, (edgeId, previous) => {
  if (previous && previous !== edgeId) removeInsertTool(previous)
  if (edgeId) addInsertTool(edgeId)
})

watch(() => props.projection, renderProjection)
watch(() => props.selectedId, renderProjection)
watch(() => props.selectedEdgeId, (edgeId) => paintEdgeSelection(edgeId ?? null))
watch(() => props.entryKey, () => applyViewport())

onBeforeUnmount(() => {
  graph?.dispose()
  graph = null
})

function applyViewport(): void {
  if (!graph || !props.entryActivity) return
  const saved = getCanvasState(props.entryActivity as never)
  if (saved) {
    graph.scale(saved.zoom)
    graph.translate(saved.panX, saved.panY)
  }
}

/** Canvas-local coordinate of the current viewport center. */
function viewportCenter(): { x: number; y: number } {
  if (!graph) return { x: 0, y: 0 }
  const size = graph.transform.getComputedSize()
  const translation = graph.translate()
  const zoom = graph.zoom()
  return {
    x: (size.width / 2 - translation.tx) / zoom,
    y: (size.height / 2 - translation.ty) / zoom,
  }
}


function renderProjection(): void {
  if (!graph) return
  // Target-node → first entry port, so edges land on the model-declared in port.
  const inPortByNode = new Map(props.projection.nodes.map((node) => [node.id, node.data.inPorts[0]]))
  const cells: Record<string, unknown>[] = []
  for (const node of props.projection.nodes) {
    cells.push({
      shape: CIKE_NODE_SHAPE,
      id: node.id,
      x: node.x,
      y: node.y,
      width: node.width,
      height: node.height,
      data: { ...node.data, selected: node.id === props.selectedId },
      ports: {
        items: [
          ...node.data.inPorts.map((port) => ({ id: port, group: "in" })),
          ...node.data.outPorts.map((port) => ({
            id: port,
            group: "out",
            attrs: { label: { text: port, x: 9, y: 3 } },
          })),
        ],
      },
    })
  }
  for (const edge of props.projection.edges) {
    const targetInPort = inPortByNode.get(edge.target)
    cells.push({
      shape: "edge",
      id: edge.id,
      zIndex: 0,
      source: edge.sourcePort ? { cell: edge.source, port: edge.sourcePort } : { cell: edge.source },
      target: targetInPort ? { cell: edge.target, port: targetInPort } : { cell: edge.target },
      connector: { name: "smooth" },
      attrs: {
        line: {
          stroke: "currentColor",
          strokeWidth: 1.5,
          targetMarker: edge.visual ? null : undefined,
          ...(edge.visual ? { strokeDasharray: "4 4" } : {}),
        },
      },
    })
  }
  graph.fromJSON({ cells })
  // fromJSON rebuilds every view, wiping classes/attrs: re-apply selection.
  paintEdgeSelection(props.selectedEdgeId ?? null)
}

function removeCellById(cellId: string): void {
  graph?.removeCell(cellId)
}

defineExpose({ viewportCenter, removeCellById })
</script>

<!-- Theme-aware edge/port styling: SVG presentation attributes cannot use
     CSS variables, so colors are applied through currentColor + CSS. -->
<style scoped>
.canvas-surface {
  background-image: radial-gradient(circle, var(--border) 1px, transparent 1px);
  background-size: 20px 20px;
}
.canvas-surface :deep(.x6-edge) {
  color: var(--muted-foreground);
}
/* Selected edge: the stroke inherits currentColor, so flipping color here
   turns the line primary in both themes without hardcoding hex values. */
.canvas-surface :deep(.x6-edge.edge-selected) {
  color: var(--primary);
}
.canvas-surface :deep(.x6-port-body circle) {
  stroke: var(--muted-foreground);
  fill: var(--card);
}
/* Entry port: its single-element markup renders the circle itself as
   .x6-port-body (no inner circle), so target it via the group's port class.
   Solid primary fill makes it a clear connection anchor. */
.canvas-surface :deep(.x6-port-in .x6-port-body) {
  fill: var(--primary);
  stroke: var(--primary);
}
.canvas-surface :deep(.x6-port-body text) {
  fill: var(--muted-foreground);
  font-size: 10px;
  user-select: none;
}
.canvas-surface :deep(.insert-btn-circle) {
  fill: var(--card);
  stroke: var(--border);
  cursor: pointer;
}
.canvas-surface :deep(.insert-btn-icon) {
  fill: none;
  stroke: var(--muted-foreground);
  stroke-width: 1.5;
  pointer-events: none;
}
.canvas-surface :deep(.insert-btn-circle:hover) {
  stroke: var(--primary);
}
.canvas-surface :deep(g:has(> .insert-btn-circle:hover) .insert-btn-icon) {
  stroke: var(--primary);
}
</style>
