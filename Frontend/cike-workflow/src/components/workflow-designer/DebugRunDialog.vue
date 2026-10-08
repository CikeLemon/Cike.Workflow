<template>
  <Dialog :open="open" @update:open="(v: boolean) => emit('update:open', v)">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>启动调试</DialogTitle>
        <DialogDescription>
          {{ hasInputs ? "填写工作流输入参数，留空的字段将由后端求值默认表达式。" : "该工作流无输入参数，将直接启动调试。" }}
        </DialogDescription>
      </DialogHeader>

      <div v-if="hasInputs" class="max-h-80 space-y-4 overflow-y-auto pr-1">
        <div v-for="input in inputs" :key="input.name" class="space-y-1.5">
          <Label :for="`debug-input-${input.name}`">
            {{ input.displayName || input.name }}
            <span v-if="input.type" class="ml-1 text-xs text-muted-foreground">{{ input.type }}{{ input.isArray ? "[]" : "" }}</span>
          </Label>

          <!-- Boolean → Switch -->
          <div v-if="isBooleanInput(input)" class="flex items-center gap-2">
            <Switch
              :id="`debug-input-${input.name}`"
              :model-value="form[input.name!] === 'true'"
              @update:model-value="(v: boolean) => { form[input.name!] = String(v) }"
            />
            <span class="text-xs text-muted-foreground">{{ form[input.name!] === "true" ? "true" : "false" }}</span>
          </div>

          <!-- JSON / Array / Object → Textarea -->
          <Textarea
            v-else-if="isJsonInput(input)"
            :id="`debug-input-${input.name}`"
            v-model="form[input.name!]"
            :placeholder="placeholderFor(input) || '输入 JSON…'"
            rows="3"
            class="font-mono text-xs"
          />

          <!-- Number → Input type=number -->
          <Input
            v-else-if="isNumberInput(input)"
            :id="`debug-input-${input.name}`"
            v-model="form[input.name!]"
            type="number"
            :placeholder="placeholderFor(input)"
          />

          <!-- String / DateTime / default → text input -->
          <Input
            v-else
            :id="`debug-input-${input.name}`"
            v-model="form[input.name!]"
            :type="input.type?.toLowerCase() === 'datetime' ? 'datetime-local' : 'text'"
            :placeholder="placeholderFor(input)"
          />

          <p v-if="input.description" class="text-xs text-muted-foreground">{{ input.description }}</p>
        </div>
      </div>

      <p v-if="error" class="max-h-24 overflow-y-auto text-xs whitespace-pre-wrap break-words text-destructive">
        {{ error }}
      </p>

      <DialogFooter>
        <Button variant="outline" :disabled="running" @click="emit('update:open', false)">取消</Button>
        <Button :disabled="running" @click="confirm">
          {{ running ? "启动中…" : "启动调试" }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { computed, reactive, watch } from "vue"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import type { InputDefinition } from "@/api/generated"

const props = defineProps<{
  open: boolean
  inputs: InputDefinition[]
  running: boolean
  error: string | null
}>()

const emit = defineEmits<{
  "update:open": [open: boolean]
  confirm: [input: Record<string, unknown>]
}>()

/** Form state: inputName → user-entered value (string representation). */
const form = reactive<Record<string, string>>({})

watch(
  () => props.open,
  (open) => {
    if (!open) return
    // Reset form and pre-fill Literal defaults
    for (const key of Object.keys(form)) delete form[key]
    for (const input of props.inputs) {
      const name = input.name ?? ""
      if (!name) continue
      const def = input.defaultValue
      if (def?.type === "Literal" && def.value != null) {
        form[name] = typeof def.value === "object" ? JSON.stringify(def.value) : String(def.value)
      } else {
        form[name] = ""
      }
    }
  },
)

function placeholderFor(input: InputDefinition): string {
  const def = input.defaultValue
  if (!def || def.type === "Literal") return ""
  const summary = typeof def.value === "string" ? def.value : JSON.stringify(def.value ?? "")
  const truncated = summary.length > 40 ? `${summary.slice(0, 40)}…` : summary
  return `默认: ${def.type} \`${truncated}\``
}

function isBooleanInput(input: InputDefinition): boolean {
  return input.type?.toLowerCase() === "boolean" && !input.isArray
}

function isNumberInput(input: InputDefinition): boolean {
  const t = input.type?.toLowerCase() ?? ""
  return (t === "number" || t === "integer" || t === "int32" || t === "int64" || t === "double" || t === "decimal") && !input.isArray
}

function isJsonInput(input: InputDefinition): boolean {
  if (input.isArray) return true
  const t = input.type?.toLowerCase() ?? ""
  return t === "object" || t === "json"
}

const hasInputs = computed(() => props.inputs.length > 0)

function buildInputPayload(): Record<string, unknown> {
  const payload: Record<string, unknown> = {}
  for (const input of props.inputs) {
    const name = input.name ?? ""
    if (!name) continue
    const raw = form[name] ?? ""
    // Empty field → don't send key (backend evaluates default expression)
    if (raw === "" && !isBooleanInput(input)) continue
    if (isBooleanInput(input)) {
      payload[name] = raw === "true"
    } else if (isNumberInput(input)) {
      const num = Number(raw)
      if (!Number.isNaN(num)) payload[name] = num
    } else if (isJsonInput(input)) {
      try { payload[name] = JSON.parse(raw) } catch { payload[name] = raw }
    } else {
      payload[name] = raw
    }
  }
  return payload
}

function confirm() {
  emit("confirm", buildInputPayload())
}
</script>
