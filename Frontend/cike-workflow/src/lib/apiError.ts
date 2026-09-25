/**
 * Extract a human-readable message from an API error response.
 *
 * The backend returns ASP.NET ProblemDetails / ValidationProblemDetails on
 * failure, e.g. `{ title, status, errors: { field: ["message"] } }`.
 * This helper surfaces the concrete validation messages instead of a generic
 * fallback so users see actionable feedback.
 */
export function extractApiErrorMessage(
  error: unknown,
  fallback = "操作失败，请稍后重试",
): string {
  if (!error || typeof error !== "object") {
    // A plain string error is already the message (e.g. a text/plain body).
    return typeof error === "string" && error.trim() ? error : fallback
  }

  const detail = error as {
    errors?: Record<string, unknown>
    detail?: unknown
    title?: unknown
  }

  // ValidationProblemDetails: errors is a dict of field -> string[]
  if (detail.errors && typeof detail.errors === "object") {
    const messages = Object.values(detail.errors)
      .flat()
      .filter((m): m is string => typeof m === "string" && m.trim().length > 0)
    if (messages.length) return messages.join("；")
  }

  if (typeof detail.detail === "string" && detail.detail.trim()) {
    return detail.detail
  }
  if (typeof detail.title === "string" && detail.title.trim()) {
    return detail.title
  }

  return fallback
}
