import { describe, expect, it } from "vitest"
import {
  buildActivityStatusMap,
  getLatestRecord,
  applyProgressEvent,
  extractActivityIdFromNodeId,
  formatDateTime,
  formatDuration,
  type ExecutionRecordLike,
  type ProgressEvent,
} from "./execution"
import type { ActivityStatus } from "@/api/generated"

describe("execution", () => {
  it("BuildStatusMap_MapsByActivityId", () => {
    const map = buildActivityStatusMap([
      { activityId: "a-start", status: 2 },
      { activityId: "a-if", status: 1 },
    ])
    expect(map.get("a-start")).toBe(2)
    expect(map.get("a-if")).toBe(1)
    expect(map.get("missing")).toBeUndefined()
  })

  it("BuildStatusMap_LaterRecordWins_FinalState", () => {
    const map = buildActivityStatusMap([
      { activityId: "a-approval", status: 1 },
      { activityId: "a-approval", status: 2 },
    ])
    expect(map.get("a-approval")).toBe(2)
  })

  it("BuildStatusMap_SkipsIncompleteRecords", () => {
    const map = buildActivityStatusMap([
      { activityId: undefined, status: 2 },
      { activityId: "a-x", status: undefined },
    ])
    expect(map.size).toBe(0)
  })
})

describe("extractActivityIdFromNodeId", () => {
  it("returns own id for root node (no colon)", () => {
    expect(extractActivityIdFromNodeId("fc-root")).toBe("fc-root")
  })

  it("returns last segment for nested node", () => {
    expect(extractActivityIdFromNodeId("fc-root:if-1:a-http")).toBe("a-http")
  })

  it("returns last segment for single-level child", () => {
    expect(extractActivityIdFromNodeId("fc-root:a-start")).toBe("a-start")
  })
})

describe("getLatestRecord", () => {
  const records: ExecutionRecordLike[] = [
    { activityId: "a-http", status: 2, createdAt: "2026-01-01T00:00:00Z", outputs: { r: 1 } },
    { activityId: "a-start", status: 2, createdAt: "2026-01-01T00:00:01Z" },
    { activityId: "a-http", status: 4, createdAt: "2026-01-01T00:00:02Z", outputs: { r: 2 } },
  ]

  it("returns the last record for a given activityId", () => {
    const rec = getLatestRecord(records, "a-http")
    expect(rec?.status).toBe(4)
    expect(rec?.outputs).toEqual({ r: 2 })
  })

  it("returns undefined for unknown activityId", () => {
    expect(getLatestRecord(records, "nope")).toBeUndefined()
  })

  it("returns the only record when there is one", () => {
    const rec = getLatestRecord(records, "a-start")
    expect(rec?.status).toBe(2)
  })
})

describe("applyProgressEvent", () => {
  it("ActivityStarted sets status to Running (1)", () => {
    const map = new Map<string, ActivityStatus>()
    const event: ProgressEvent = { type: 0, activityNodeId: "fc-root:a-http" }
    const next = applyProgressEvent(map, event)
    expect(next.get("a-http")).toBe(1)
  })

  it("ActivityCompleted sets status to Completed (2)", () => {
    const map = new Map<string, ActivityStatus>([["a-http", 1]])
    const event: ProgressEvent = { type: 1, activityNodeId: "fc-root:a-http" }
    const next = applyProgressEvent(map, event)
    expect(next.get("a-http")).toBe(2)
  })

  it("ActivitySuspended sets status to Running (1) with suspended flag", () => {
    const map = new Map<string, ActivityStatus>()
    const event: ProgressEvent = { type: 2, activityNodeId: "fc-root:a-approval" }
    const next = applyProgressEvent(map, event)
    // Suspended maps to Running(1) in ActivityStatus enum; the UI distinguishes via event type
    expect(next.get("a-approval")).toBe(1)
  })

  it("ActivityFaulted sets status to Faulted (4)", () => {
    const map = new Map<string, ActivityStatus>([["a-http", 1]])
    const event: ProgressEvent = { type: 3, activityNodeId: "fc-root:a-http" }
    const next = applyProgressEvent(map, event)
    expect(next.get("a-http")).toBe(4)
  })

  it("ignores events without activityNodeId", () => {
    const map = new Map<string, ActivityStatus>([["a-x", 2]])
    const event: ProgressEvent = { type: 0, activityNodeId: null }
    const next = applyProgressEvent(map, event)
    expect(next).toBe(map) // same reference, no mutation
  })

  it("ignores workflow-level terminal events (type >= 4)", () => {
    const map = new Map<string, ActivityStatus>([["a-x", 2]])
    const event: ProgressEvent = { type: 4, activityNodeId: "fc-root:a-x" }
    const next = applyProgressEvent(map, event)
    expect(next).toBe(map)
  })

  it("does not mutate the original map", () => {
    const map = new Map<string, ActivityStatus>([["a-http", 1]])
    const event: ProgressEvent = { type: 1, activityNodeId: "a-http" }
    applyProgressEvent(map, event)
    expect(map.get("a-http")).toBe(1) // original unchanged
  })
})

describe("formatDateTime", () => {
  it("formats a valid ISO timestamp", () => {
    expect(formatDateTime("2026-01-01T08:30:00Z")).toBeTruthy()
  })

  it("returns null for missing or invalid values", () => {
    expect(formatDateTime(null)).toBeNull()
    expect(formatDateTime(undefined)).toBeNull()
    expect(formatDateTime("")).toBeNull()
    expect(formatDateTime("not-a-date")).toBeNull()
  })

  it("treats .NET DateTime.MinValue (unset finishedAt) as no value", () => {
    expect(formatDateTime("0001-01-01T00:00:00")).toBeNull()
    expect(formatDateTime("0001-01-01T00:00:00Z")).toBeNull()
  })
})

describe("formatDuration", () => {
  it("formats sub-second durations in ms", () => {
    expect(formatDuration("2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.350Z")).toBe("350ms")
  })

  it("formats sub-minute durations in seconds", () => {
    expect(formatDuration("2026-01-01T00:00:00Z", "2026-01-01T00:00:01.200Z")).toBe("1.2s")
  })

  it("formats multi-minute durations in 分/秒", () => {
    expect(formatDuration("2026-01-01T00:00:00Z", "2026-01-01T00:02:05Z")).toBe("2分 5秒")
    expect(formatDuration("2026-01-01T00:00:00Z", "2026-01-01T00:02:00Z")).toBe("2分")
  })

  it("returns null when either side is missing, invalid, or negative", () => {
    expect(formatDuration(null, "2026-01-01T00:00:01Z")).toBeNull()
    expect(formatDuration("2026-01-01T00:00:00Z", undefined)).toBeNull()
    expect(formatDuration("bad", "2026-01-01T00:00:01Z")).toBeNull()
    expect(formatDuration("2026-01-01T00:00:02Z", "2026-01-01T00:00:01Z")).toBeNull()
  })
})
