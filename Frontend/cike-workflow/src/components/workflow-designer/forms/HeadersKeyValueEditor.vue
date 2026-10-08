<template>
  <div class="space-y-1.5">
    <div v-for="(row, index) in rows" :key="index" class="flex items-center gap-1.5">
      <UiInput
        class="h-8 w-28 shrink-0 text-xs"
        :model-value="row.key"
        placeholder="Header"
        :disabled="readonly"
        @change="(event: Event) => updateRow(index, { key: (event.target as HTMLInputElement).value })"
      />
      <UiInput
        class="h-8 min-w-0 flex-1 text-xs"
        :model-value="row.value"
        placeholder="值（逗号分隔多值）"
        :disabled="readonly"
        @change="(event: Event) => updateRow(index, { value: (event.target as HTMLInputElement).value })"
      />
      <Button variant="ghost" size="icon" class="size-8 shrink-0" :disabled="readonly" @click="removeRow(index)">
        <Trash2 :size="13" />
      </Button>
    </div>
    <Button variant="outline" size="sm" class="w-full" :disabled="readonly" @click="addRow">
      <Plus :size="13" class="mr-1" />添加请求头
    </Button>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from "vue"
import { Button } from "@/components/ui/button"
import { Input as UiInput } from "@/components/ui/input"
import { Plus, Trash2 } from "@lucide/vue"

/**
 * Key-value editor for the requestHeaders Literal, whose value is the
 * CustomHttpHeaders shape `{ headers: Record<string, string[]> }` — far too
 * nested to hand-edit as JSON. Reads tolerate both the canonical shape and a
 * flat string map; writes always emit the canonical shape. The whole table is
 * committed per row change (blur) / add / remove, like SwitchForm's cases.
 */
const props = withDefaults(defineProps<{ value: unknown; readonly?: boolean }>(), {
  readonly: false,
})
const emit = defineEmits<{ (e: "commit", next: { headers: Record<string, string[]> }): void }>()

interface HeaderRow {
  key: string
  value: string
}

function sourceMap(value: unknown): Record<string, unknown> | null {
  if (value == null || typeof value !== "object") return null
  const nested = (value as { headers?: unknown }).headers
  if (nested != null && typeof nested === "object") return nested as Record<string, unknown>
  return value as Record<string, unknown>
}

function rowsOf(value: unknown): HeaderRow[] {
  const source = sourceMap(value)
  if (!source) return []
  const rows: HeaderRow[] = []
  for (const [key, entry] of Object.entries(source)) {
    if (typeof entry === "string") rows.push({ key, value: entry })
    else if (Array.isArray(entry)) rows.push({ key, value: entry.map(String).join(", ") })
  }
  return rows
}

const rows = ref<HeaderRow[]>(rowsOf(props.value))
// External model mutations (undo/redo, node switch, commit normalization)
// arrive as a new prop; resync unless the meaningful rows already match, so
// in-progress empty rows survive their own no-op commits.
watch(
  () => props.value,
  (next) => {
    const synced = rowsOf(next)
    const meaningful = rows.value.filter((row) => row.key.trim() !== "" || row.value !== "")
    if (JSON.stringify(synced) !== JSON.stringify(meaningful)) rows.value = synced
  },
)

function commitRows(next: HeaderRow[]): void {
  if (props.readonly) return
  rows.value = next
  const headers: Record<string, string[]> = {}
  for (const row of next) {
    const key = row.key.trim()
    if (!key) continue
    headers[key] = row.value.split(",").map((entry) => entry.trim()).filter(Boolean)
  }
  emit("commit", { headers })
}

function updateRow(index: number, patch: Partial<HeaderRow>): void {
  commitRows(rows.value.map((row, i) => (i === index ? { ...row, ...patch } : row)))
}

function addRow(): void {
  commitRows([...rows.value, { key: "", value: "" }])
}

function removeRow(index: number): void {
  commitRows(rows.value.filter((_, i) => i !== index))
}
</script>
