<template>
  <div class="space-y-2">
    <Button
      variant="outline"
      size="sm"
      class="w-full justify-between text-xs font-normal"
      :disabled="readonly"
      @click="open = !open"
    >
      <span class="truncate">{{ summary }}</span>
      <ChevronDown class="size-3.5 shrink-0 text-muted-foreground transition-transform" :class="{ 'rotate-180': open }" />
    </Button>

    <div v-if="open" class="space-y-2 rounded-md border p-2">
      <div class="flex gap-1">
        <Button variant="ghost" size="sm" class="h-6 flex-1 px-1 text-[10px]" :disabled="readonly" @click="selectGroup(400, 500)">
          全选 4xx
        </Button>
        <Button variant="ghost" size="sm" class="h-6 flex-1 px-1 text-[10px]" :disabled="readonly" @click="selectGroup(500, 600)">
          全选 5xx
        </Button>
        <Button variant="ghost" size="sm" class="h-6 flex-1 px-1 text-[10px]" :disabled="readonly" @click="clearAll">
          清空
        </Button>
      </div>

      <div class="grid grid-cols-5 gap-1">
        <Button
          v-for="code in COMMON_CODES"
          :key="code"
          :variant="selected.includes(code) ? 'secondary' : 'outline'"
          size="sm"
          class="h-6 px-0 text-[10px]"
          :disabled="readonly"
          @click="toggle(code)"
        >
          {{ code }}
        </Button>
      </div>

      <div v-if="extraCodes.length" class="flex flex-wrap gap-1">
        <span
          v-for="code in extraCodes"
          :key="code"
          class="bg-secondary text-secondary-foreground flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[10px]"
        >
          {{ code }}
          <button
            v-if="!readonly"
            class="text-muted-foreground hover:text-foreground"
            @click="remove(code)"
          >
            <X class="size-3" />
          </button>
        </span>
      </div>

      <div class="flex gap-1">
        <UiInput
          v-model="customRaw"
          type="number"
          class="h-7 flex-1 text-xs"
          placeholder="自定义 100-599"
          :disabled="readonly"
          @keydown.enter.prevent="addCustom"
        />
        <Button variant="outline" size="sm" class="h-7 px-2" :disabled="readonly" @click="addCustom">
          <Plus class="size-3" />
        </Button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue"
import { Button } from "@/components/ui/button"
import { Input as UiInput } from "@/components/ui/input"
import { ChevronDown, Plus, X } from "@lucide/vue"
import { DEFAULT_RESPONSE_ERROR_CODES } from "@/core/activities/SendHttpRequest"

/**
 * Compact multi-select for HTTP status-code lists (responseErrorCodes /
 * suspendOnStatusCodes). The default error list has ~45 codes, far too many
 * for a comma-separated input in the narrow property panel, so selection runs
 * through common-code chips plus 4xx/5xx bulk actions; the whole array is
 * handed back in one commit so undo stays one step per edit session.
 */
const props = withDefaults(defineProps<{ modelValue: number[]; readonly?: boolean }>(), {
  readonly: false,
})
const emit = defineEmits<{ (e: "commit", next: number[]): void }>()

// Three tidy rows of five in the chip grid; anything else selected shows up
// in the "extra" chip row below.
const COMMON_CODES = [400, 401, 403, 404, 405, 408, 409, 410, 415, 422, 429, 500, 502, 503, 504]

const selected = computed(() => props.modelValue ?? [])
const open = ref(false)
const customRaw = ref("")

const summary = computed(() => {
  const codes = selected.value
  if (codes.length === 0) return "未设置"
  if (codes.length <= 6) return codes.join(", ")
  return `${codes.length} 个状态码`
})

const extraCodes = computed(() =>
  selected.value.filter((code) => !COMMON_CODES.includes(code)).sort((a, b) => a - b),
)

function commitNext(next: number[]): void {
  if (props.readonly) return
  emit("commit", [...new Set(next)].sort((a, b) => a - b))
}

function toggle(code: number): void {
  commitNext(
    selected.value.includes(code) ? selected.value.filter((c) => c !== code) : [...selected.value, code],
  )
}

function selectGroup(from: number, to: number): void {
  const group = DEFAULT_RESPONSE_ERROR_CODES.filter((code) => code >= from && code < to)
  commitNext([...selected.value, ...group])
}

function clearAll(): void {
  commitNext([])
}

function remove(code: number): void {
  commitNext(selected.value.filter((c) => c !== code))
}

function addCustom(): void {
  const code = Number(customRaw.value)
  if (!Number.isInteger(code) || code < 100 || code > 599) return
  if (!selected.value.includes(code)) commitNext([...selected.value, code])
  customRaw.value = ""
}
</script>
