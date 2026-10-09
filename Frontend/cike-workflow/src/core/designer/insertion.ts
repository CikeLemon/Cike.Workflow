import type { ActivityConnection } from "../models/ActivityConnection";
import type { DesignerNodeMeta } from "./metadata";

/**
 * Pure insertion plan for edge insertion (ADR 0013): which nodes make room for
 * a node inserted mid-connection, by how much, and where the new node lands.
 * Holds no mutation — the composable turns the plan into commands.
 */

/** Horizontal breathing room required on each side of the inserted node. */
export const INSERT_MARGIN = 40;

/**
 * Connection identity: projected edge ids embed the array index and cannot
 * serve as identity, so callers pass the endpoint triple instead.
 */
export interface ConnectionRef {
  source: string;
  sourcePort?: string;
  target: string;
}

/** The model connection matching an endpoint triple, or undefined. */
export function findConnectionByRef(connections: ActivityConnection[], ref: ConnectionRef): ActivityConnection | undefined {
  return connections.find(
    (connection) =>
      connection.source.activityId === ref.source &&
      (connection.source.port ?? undefined) === (ref.sourcePort ?? undefined) &&
      connection.target.activityId === ref.target,
  );
}

export interface NodeRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Structural downstream of the connection target: nodes reachable from the
 * target over the old graph minus the connection being replaced, excluding
 * nodes that can flow back to the source (cycle guard — shifting those would
 * move the source side along and open no gap at all).
 */
export function computeShiftIds(
  connections: ActivityConnection[],
  removed: ActivityConnection,
  sourceId: string,
  targetId: string,
): string[] {
  const rest = connections.filter((connection) => connection !== removed);
  const outbound = new Map<string, string[]>();
  const inbound = new Map<string, string[]>();
  for (const connection of rest) {
    outbound.set(connection.source.activityId, [...(outbound.get(connection.source.activityId) ?? []), connection.target.activityId]);
    inbound.set(connection.target.activityId, [...(inbound.get(connection.target.activityId) ?? []), connection.source.activityId]);
  }
  const reachable = traverse(outbound, targetId);
  // Reflexive: the source itself is always excluded from the shift set.
  const canFlowToSource = traverse(inbound, sourceId);
  return [...reachable].filter((id) => !canFlowToSource.has(id));
}

function traverse(adjacency: Map<string, string[]>, start: string): Set<string> {
  const seen = new Set<string>([start]);
  const queue = [start];
  while (queue.length > 0) {
    for (const next of adjacency.get(queue.shift()!) ?? []) {
      if (seen.has(next)) continue;
      seen.add(next);
      queue.push(next);
    }
  }
  return seen;
}

/** Rightward shift opening exactly enough gap; 0 when the gap already fits. */
export function computeShiftDelta(source: NodeRect, target: NodeRect, newWidth: number): number {
  const gap = target.x - (source.x + source.width);
  return Math.max(0, newWidth + 2 * INSERT_MARGIN - gap);
}

/** Center of the gap between the source and the (shifted) target. */
export function computeInsertPosition(
  source: NodeRect,
  target: NodeRect,
  appliedDelta: number,
  newSize: { width: number; height: number },
): DesignerNodeMeta {
  const sourceCx = source.x + source.width / 2;
  const targetCx = target.x + appliedDelta + target.width / 2;
  const sourceCy = source.y + source.height / 2;
  const targetCy = target.y + target.height / 2;
  return {
    x: (sourceCx + targetCx) / 2 - newSize.width / 2,
    y: (sourceCy + targetCy) / 2 - newSize.height / 2,
  };
}
