import {
  HubConnectionBuilder,
  HubConnectionState,
  type HubConnection,
} from "@microsoft/signalr"
import type { ProgressEvent } from "@/core/designer/execution"

/**
 * App-level singleton SignalR connection to the workflow realtime hub.
 *
 * Architecture decision: one connection for the entire SPA; pages express
 * subscription intent via watch/unwatch (group join/leave). Connection is
 * lazily established on first watch() call and kept alive for the app lifetime.
 */

const HUB_PATH = "/realtime/workflow"
const PROGRESS_METHOD = "ExecutionProgress"

type ProgressListener = (event: ProgressEvent) => void

let connection: HubConnection | null = null
let connectionPromise: Promise<void> | null = null
const listeners = new Set<ProgressListener>()

function getHubUrl(): string {
  const base = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "")
  return `${base}${HUB_PATH}`
}

function ensureConnection(): { conn: HubConnection; ready: Promise<void> } {
  if (connection) return { conn: connection, ready: connectionPromise ?? Promise.resolve() }

  const builder = new HubConnectionBuilder()
    .withUrl(getHubUrl(), {
      accessTokenFactory: () => localStorage.getItem("token") ?? "",
    })
    .withAutomaticReconnect()

  connection = builder.build()

  connection.on(PROGRESS_METHOD, (event: ProgressEvent) => {
    for (const listener of listeners) listener(event)
  })

  connectionPromise = connection.start().catch((err) => {
    console.warn("[signalr] connection failed:", err)
    connectionPromise = null
  })

  return { conn: connection, ready: connectionPromise }
}

/** Subscribe to progress events. Returns an unsubscribe function. */
export function onProgress(listener: ProgressListener): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** Join an instance group to receive its execution progress events. */
export async function watch(instanceId: string): Promise<void> {
  const { conn, ready } = ensureConnection()
  await ready
  if (conn.state === HubConnectionState.Connected) {
    await conn.invoke("Watch", instanceId)
  }
}

/** Leave an instance group to stop receiving its execution progress events. */
export async function unwatch(instanceId: string): Promise<void> {
  if (!connection || connection.state !== HubConnectionState.Connected) return
  await connection.invoke("Unwatch", instanceId).catch(() => {
    // Best-effort; server cleans up on disconnect anyway
  })
}
