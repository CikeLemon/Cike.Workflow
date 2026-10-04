import type { ActivityStatus } from "@/api/generated"

/**
 * Pure mapping from a workflow instance's activity execution records to the
 * per-activity run status shown on the read-only instance canvas. Kept free of
 * DOM/X6 so it is testable in isolation, mirroring the projection module.
 */

export interface ExecutionRecordLike {
  activityId?: string
  status?: ActivityStatus
  createdAt?: string
  outputs?: Record<string, unknown> | null
  activityState?: Record<string, unknown> | null
  exception?: unknown
  activityName?: string
  activityType?: string
}

/**
 * activityId → latest ActivityStatus. Records arrive chronologically, so a
 * later record for the same activity overrides an earlier one (final state).
 */
export function buildActivityStatusMap(records: ExecutionRecordLike[]): Map<string, ActivityStatus> {
  const map = new Map<string, ActivityStatus>()
  for (const record of records) {
    if (!record.activityId || record.status == null) continue
    map.set(record.activityId, record.status)
  }
  return map
}

/** UI label + semantic token class per ActivityStatus (0..4). */
export const ACTIVITY_STATUS_UI: Record<ActivityStatus, { label: string; class: string }> = {
  0: { label: "等待", class: "bg-muted text-muted-foreground" },
  1: { label: "运行中", class: "bg-info/15 text-info" },
  2: { label: "已完成", class: "bg-success/15 text-success" },
  3: { label: "已取消", class: "bg-muted text-muted-foreground" },
  4: { label: "故障", class: "bg-destructive/15 text-destructive" },
}

// ---------------------------------------------------------------------------
// Real-time execution progress
// ---------------------------------------------------------------------------

/**
 * SignalR progress event payload (mirrors backend WorkflowExecutionProgressEvent).
 * Type enum: 0=ActivityStarted, 1=ActivityCompleted, 2=ActivitySuspended,
 * 3=ActivityFaulted, 4=WorkflowFinished, 5=WorkflowSuspended,
 * 6=WorkflowFaulted, 7=WorkflowCanceled.
 */
export interface ProgressEvent {
  type: number
  activityNodeId: string | null
  activityInstanceId?: string | null
  timestamp?: string
}

/**
 * Extract the activity's own id from a hierarchical NodeId.
 * NodeId format: "ancestor1:ancestor2:ownId" (colon-separated, ADR 0003).
 */
export function extractActivityIdFromNodeId(nodeId: string): string {
  const lastColon = nodeId.lastIndexOf(":")
  return lastColon >= 0 ? nodeId.slice(lastColon + 1) : nodeId
}

/** Map progress event type to ActivityStatus. */
const EVENT_TYPE_TO_STATUS: Record<number, ActivityStatus> = {
  0: 1, // ActivityStarted → Running
  1: 2, // ActivityCompleted → Completed
  2: 1, // ActivitySuspended → Running (UI distinguishes via event type)
  3: 4, // ActivityFaulted → Faulted
}

/**
 * Pure reducer: apply a SignalR progress event to the status map.
 * Returns a NEW map (no mutation) for activity-level events (type 0..3);
 * returns the SAME reference for workflow-level events or missing nodeId.
 */
export function applyProgressEvent(
  statusMap: Map<string, ActivityStatus>,
  event: ProgressEvent,
): Map<string, ActivityStatus> {
  if (event.type >= 4 || !event.activityNodeId) return statusMap
  const activityId = extractActivityIdFromNodeId(event.activityNodeId)
  const status = EVENT_TYPE_TO_STATUS[event.type]
  if (status == null) return statusMap
  const next = new Map(statusMap)
  next.set(activityId, status)
  return next
}

/**
 * Format an ISO timestamp for compact display (locale date-time, seconds
 * precision). Returns null for missing/invalid values so callers can hide the
 * slot instead of rendering "Invalid Date". Also treats .NET's unset sentinel
 * (DateTime.MinValue → year 1) as "no value" so a not-yet-finished instance
 * does not show "结束 1/1/1".
 */
export function formatDateTime(iso: string | null | undefined): string | null {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime()) || d.getFullYear() <= 1) return null
  return d.toLocaleString()
}

/**
 * Human-readable duration between two ISO timestamps (e.g. "1.2s", "350ms",
 * "2分 5秒"). Returns null when either side is missing/invalid or negative.
 */
export function formatDuration(startIso: string | null | undefined, endIso: string | null | undefined): string | null {
  if (!startIso || !endIso) return null
  const start = new Date(startIso).getTime()
  const end = new Date(endIso).getTime()
  if (Number.isNaN(start) || Number.isNaN(end) || end < start) return null
  const ms = end - start
  if (ms < 1000) return `${ms}ms`
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`
  const totalSeconds = Math.round(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return seconds > 0 ? `${minutes}分 ${seconds}秒` : `${minutes}分`
}

/**
 * Given chronologically-ordered execution records, return the latest one
 * for the specified activityId. Returns undefined if no match.
 */
export function getLatestRecord(
  records: ExecutionRecordLike[],
  activityId: string,
): ExecutionRecordLike | undefined {
  let result: ExecutionRecordLike | undefined
  for (const record of records) {
    if (record.activityId === activityId) result = record
  }
  return result
}
