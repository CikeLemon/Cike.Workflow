import type { Activity, IActivity } from "../abstracts/Activity";
import type { ActivityConnection } from "../models/ActivityConnection";
import { Flowchart } from "../activities/Flowchart";
import { activityShortName, resolveActivityClass } from "./registry";
import { computeAutoLayout, type Point } from "./layout";
import { getNodePosition, getNodeSize } from "./metadata";

/**
 * Pure projection from the domain model (a Flowchart level) to canvas cell
 * data. Produces plain JSON — never an X6 Graph instance — so it stays
 * testable without DOM/X6 and the component layer owns Graph construction.
 */

export interface DesignerNodeData {
  activityId: string;
  /** Wire type, e.g. "Cike.If". */
  type: string;
  /** Short type name, e.g. "If". */
  typeShort: string;
  name: string;
  /** Backend-provided icon name (lucide); enriched by the composable, not the
   *  pure projection (which has no descriptor access). */
  icon?: string | null;
  /** Entry port names (connection targets). */
  inPorts: string[];
  /** Outcome port names (connection sources). */
  outPorts: string[];
  /** True when the type has no frontend mirror (renders as generic node). */
  isGeneric: boolean;
  /** True when double-click drills into a nested level. */
  canDrill: boolean;
  /** Run status when shown on a read-only instance canvas (0..4); else absent. */
  status?: import("@/api/generated").ActivityStatus | null;
  /** True when this activity has no execution record on the read-only instance
   *  canvas — rendered as an explicit "未执行" badge so every node shows a
   *  status. Only ever set by the instance projection; the designer leaves it
   *  undefined. */
  notRun?: boolean;
  /** True when backend canvas validation flagged this activity (error badge). */
  hasError?: boolean;
}

export interface ProjectedNode {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  data: DesignerNodeData;
}

export interface ProjectedEdge {
  id: string;
  source: string;
  sourcePort?: string;
  target: string;
  /** Visual-only edges (e.g. sequence chains) never enter the model. */
  visual?: boolean;
}

export interface CanvasProjection {
  nodes: ProjectedNode[];
  edges: ProjectedEdge[];
}

const EMPTY_PROJECTION: CanvasProjection = { nodes: [], edges: [] };

/** Default node dimensions (mirrors register.ts constants). */
const DEFAULT_NODE_WIDTH = 180;
const DEFAULT_NODE_HEIGHT = 44;
/** Vertical space each port occupies on the node edge. */
const PORT_SPACING = 22;
/** Top+bottom padding inside the port area. */
const NODE_PADDING = 12;

/** Compute the minimum height needed to display ports without squeezing. */
function computeMinHeight(inPortCount: number, outPortCount: number): number {
  const maxPorts = Math.max(inPortCount, outPortCount);
  return Math.max(DEFAULT_NODE_HEIGHT, maxPorts * PORT_SPACING + NODE_PADDING);
}

export function getOutPortsOf(activity: IActivity): string[] {
  // Only fall back to a default outcome for non-instances (e.g. plain wire
  // objects); a real activity's declared ports are trusted as-is (End = none).
  if (typeof (activity as Activity).getOutPorts !== "function") return ["Done"];
  return (activity as Activity).getOutPorts().map((port) => port.name);
}

export function getInPortsOf(activity: IActivity): string[] {
  if (typeof (activity as Activity).getInPorts !== "function") return ["In"];
  return (activity as Activity).getInPorts().map((port) => port.name);
}

export function canDrillInto(activity: IActivity): boolean {
  if (activity instanceof Flowchart) return true;
  // Generic unknowns are drillable only when their wire JSON was container-like.
  const generic = activity as { raw?: { activities?: unknown } };
  if (typeof generic.raw === "object" && generic.raw !== null) return Array.isArray(generic.raw.activities);
  // Property-held bodies (ForEach.body / While.body / For.body) are drillable;
  // a null body gets an empty flowchart created on drill-in.
  return "body" in activity;
}

export function projectFlowchart(flowchart: Flowchart): CanvasProjection {
  return projectCanvas(flowchart);
}

/** Duck-typed source: any container-shaped model (mirrored or generic). */
export interface CanvasSource {
  activities: IActivity[];
  connections: ActivityConnection[];
}

export function projectCanvas(source: CanvasSource): CanvasProjection {
  const activities = source.activities;
  const connections = source.connections ?? [];
  if (activities.length === 0 && connections.length === 0) return EMPTY_PROJECTION;

  const savedPositions = new Map<string, Point>();
  for (const activity of activities) {
    const saved = getNodePosition(activity);
    if (saved) savedPositions.set(activity.id, saved);
  }
  const layout = computeAutoLayout(activities, connections);

  const nodes: ProjectedNode[] = activities.map((activity) => {
    const position = savedPositions.get(activity.id) ?? layout.get(activity.id) ?? { x: 80, y: 80 };
    const inPorts = getInPortsOf(activity);
    const outPorts = getOutPortsOf(activity);
    const minHeight = computeMinHeight(inPorts.length, outPorts.length);
    const savedSize = getNodeSize(activity);
    return {
      id: activity.id,
      x: position.x,
      y: position.y,
      width: savedSize ? Math.max(savedSize.width, DEFAULT_NODE_WIDTH) : DEFAULT_NODE_WIDTH,
      height: savedSize ? Math.max(savedSize.height, minHeight) : minHeight,
      data: {
        activityId: activity.id,
        type: activity.type,
        typeShort: activityShortName(activity.type),
        name: activity.name ?? activityShortName(activity.type),
        inPorts,
        outPorts,
        isGeneric: resolveActivityClass(activity.type) === null,
        canDrill: canDrillInto(activity),
      },
    };
  });

  const edges: ProjectedEdge[] = connections.map((connection, index) => ({
    id: `conn-${index}-${connection.source.activityId}-${connection.target.activityId}`,
    source: connection.source.activityId,
    sourcePort: connection.source.port,
    target: connection.target.activityId,
  }));

  return { nodes, edges };
}

/**
 * Read-only ordered chain for containers without connections (e.g. Sequence
 * drill-in): children render top-to-bottom with visual edges that never enter
 * the model — reordering happens through the property panel instead.
 */
export function projectOrderedChain(children: IActivity[]): CanvasProjection {
  if (children.length === 0) return EMPTY_PROJECTION;
  const nodes: ProjectedNode[] = children.map((activity, index) => {
    const inPorts = getInPortsOf(activity);
    const outPorts = getOutPortsOf(activity);
    const minHeight = computeMinHeight(inPorts.length, outPorts.length);
    const savedSize = getNodeSize(activity);
    return {
      id: activity.id,
      x: 200,
      y: 80 + index * 140,
      width: savedSize ? Math.max(savedSize.width, DEFAULT_NODE_WIDTH) : DEFAULT_NODE_WIDTH,
      height: savedSize ? Math.max(savedSize.height, minHeight) : minHeight,
      data: {
        activityId: activity.id,
        type: activity.type,
        typeShort: activityShortName(activity.type),
        name: activity.name ?? activityShortName(activity.type),
        inPorts,
        outPorts,
        isGeneric: resolveActivityClass(activity.type) === null,
        canDrill: canDrillInto(activity),
      },
    };
  });
  const edges: ProjectedEdge[] = [];
  for (let i = 0; i < children.length - 1; i += 1) {
    edges.push({
      id: `chain-${i}-${children[i].id}-${children[i + 1].id}`,
      source: children[i].id,
      target: children[i + 1].id,
      visual: true,
    });
  }
  return { nodes, edges };
}
