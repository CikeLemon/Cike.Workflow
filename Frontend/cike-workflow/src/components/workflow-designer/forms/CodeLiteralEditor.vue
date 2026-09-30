<script setup lang="ts">
import { ref, watch } from "vue"
import { Maximize2 } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import MonacoEditor from "../MonacoEditor.vue"

/**
 * Literal-slot editor for code-shaped values (JSON bodies, JS scripts):
 * Monaco with syntax highlighting, edited as a local draft and handed back
 * raw on blur so the caller commits one undo step per edit session (same
 * convention as ExpressionEditor's own Monaco branch). The caller keeps the
 * coercion/commit because only it knows the Input's target type.
 * An expand button opens the same draft in a near-fullscreen dialog, because
 * the inline 120px slot is too small for real code editing.
 */
const props = withDefaults(
  defineProps<{ value: unknown; language?: string; readonly?: boolean; height?: string; title?: string }>(),
  { language: "json", readonly: false, height: "120px" },
)
const emit = defineEmits<{ (e: "blur", raw: string): void }>()

// Objects render pretty-printed so long JSON stays readable in Monaco; the
// committed model is parsed back, so formatting is display-only.
function asText(value: unknown): string {
  if (value == null) return ""
  if (typeof value === "object") return JSON.stringify(value, null, 2)
  return String(value)
}

const draft = ref(asText(props.value))
// External model mutations (undo/redo, node switch, commit normalization)
// arrive as a new prop; resync the draft unless it already matches.
watch(
  () => props.value,
  (next) => {
    const text = asText(next)
    if (text !== draft.value) draft.value = text
  },
)

const expanded = ref(false)
// Closing the fullscreen dialog ends the edit session too: the dialog editor
// is disposed without a blur event, so commit here or edits made inside the
// dialog would never reach the model.
watch(expanded, (open) => {
  if (!open) emit("blur", draft.value)
})
</script>

<template>
  <div class="relative">
    <MonacoEditor
      v-model="draft"
      :language="language"
      :readonly="readonly"
      :height="height"
      @blur="emit('blur', draft)"
    />
    <Button
      type="button"
      variant="ghost"
      size="icon-xs"
      class="bg-background/70 text-muted-foreground hover:text-foreground absolute top-1 right-1 z-10 backdrop-blur-sm dark:hover:bg-accent/50"
      title="放大编辑"
      @click="expanded = true"
    >
      <Maximize2 />
      <span class="sr-only">放大编辑</span>
    </Button>

    <Dialog v-model:open="expanded">
      <!-- Inset positioning without the default centering translate: a
           transformed ancestor would become the containing block for Monaco's
           fixed-position suggest widget and misplace it. -->
      <DialogContent
        class="top-8 right-8 bottom-8 left-8 flex max-w-none w-auto translate-x-0 translate-y-0 flex-col gap-2 p-3 sm:max-w-none"
      >
        <DialogHeader>
          <DialogTitle class="text-sm">{{ title ?? "代码编辑" }}</DialogTitle>
        </DialogHeader>
        <div class="min-h-0 flex-1">
          <MonacoEditor
            v-model="draft"
            :language="language"
            :readonly="readonly"
            height="100%"
            @blur="emit('blur', draft)"
          />
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
