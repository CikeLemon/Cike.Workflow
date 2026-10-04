<script setup lang="ts">
import { computed } from "vue"
import { Badge } from "@/components/ui/badge"
import type { ArgumentDefinition } from "@/api/generated"

/**
 * Schema-driven workflow argument block (input or output) for the instance
 * overview. Always rendered — the three states are explicit:
 * - declared + value present  → value shown
 * - declared + no value       → "无值" placeholder
 * - no declarations at all    → "未声明" hint
 * When the run state itself is unavailable (backend could not restore it),
 * every declared row degrades to "状态不可用" instead of silently vanishing.
 */
const props = defineProps<{
  title: string
  defs: ArgumentDefinition[]
  values: Record<string, unknown> | null | undefined
  /** False when workflowState is missing — values are then unknown, not absent. */
  stateAvailable: boolean
}>()

interface IoRow {
  name: string
  displayName?: string
  type?: string
  hasValue: boolean
  value: unknown
}

const rows = computed<IoRow[]>(() => {
  const declared = props.defs.map((d) => {
    const name = d.name ?? ""
    const hasValue = props.values != null && name in props.values
    return {
      name,
      displayName: d.displayName || undefined,
      type: d.type || undefined,
      hasValue,
      value: hasValue ? props.values?.[name] : undefined,
    }
  })
  // Runtime values not covered by the schema (e.g. definition changed after start)
  const declaredNames = new Set(props.defs.map((d) => d.name))
  const extra = Object.entries(props.values ?? {})
    .filter(([key]) => !declaredNames.has(key))
    .map(([key, value]) => ({ name: key, hasValue: true, value }))
  return [...declared, ...extra]
})

function formatValue(value: unknown): string {
  if (typeof value === "string") return value
  try { return JSON.stringify(value) } catch { return String(value) }
}
</script>

<template>
  <section>
    <h4 class="mb-1 text-xs font-semibold text-muted-foreground">{{ title }}</h4>
    <div v-if="rows.length" class="space-y-1.5">
      <div
        v-for="row in rows"
        :key="row.name"
        class="rounded border bg-card px-2 py-1.5"
      >
        <div class="flex items-center gap-1.5 text-xs">
          <span class="truncate font-medium">{{ row.displayName || row.name }}</span>
          <span v-if="row.type" class="shrink-0 rounded bg-muted px-1 py-px font-mono text-[10px] text-muted-foreground">{{ row.type }}</span>
          <Badge v-if="!stateAvailable" variant="outline" class="ml-auto shrink-0 border-dashed px-1.5 py-0 text-[10px] font-normal text-muted-foreground">状态不可用</Badge>
          <Badge v-else-if="!row.hasValue" variant="outline" class="ml-auto shrink-0 border-dashed px-1.5 py-0 text-[10px] font-normal text-muted-foreground">无值</Badge>
        </div>
        <pre
          v-if="stateAvailable && row.hasValue"
          class="mt-1 max-h-32 overflow-auto whitespace-pre-wrap break-all font-mono text-[11px] text-foreground"
        >{{ formatValue(row.value) }}</pre>
      </div>
    </div>
    <p v-else class="rounded border border-dashed px-2 py-1.5 text-xs text-muted-foreground">
      {{ stateAvailable ? "该工作流未声明参数" : "运行状态不可用" }}
    </p>
  </section>
</template>
