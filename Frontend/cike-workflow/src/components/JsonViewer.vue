<template>
  <div class="relative">
    <MonacoEditor
      :model-value="text"
      language="json"
      readonly
      :height="height"
      :word-wrap="wrapOn"
      @ready="registerWrapToggle"
    />
    <div class="absolute top-1 right-1 z-10 flex gap-0.5">
      <Button
        type="button"
        variant="ghost"
        size="icon-xs"
        class="bg-background/70 backdrop-blur-sm dark:hover:bg-accent/50"
        :class="wrapOn ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'"
        :title="wrapOn ? '关闭自动换行' : '开启自动换行'"
        @click="wrapOn = !wrapOn"
      >
        <TextWrap />
        <span class="sr-only">自动换行</span>
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon-xs"
        class="bg-background/70 text-muted-foreground hover:text-foreground backdrop-blur-sm dark:hover:bg-accent/50"
        title="放大查看"
        @click="expanded = true"
      >
        <Maximize2 />
        <span class="sr-only">放大查看</span>
      </Button>
    </div>

    <Dialog v-model:open="expanded">
      <!-- Inset positioning without the default centering translate: a
           transformed ancestor would become the containing block for Monaco's
           fixed-position suggest widget and misplace it. -->
      <DialogContent
        class="top-8 right-8 bottom-8 left-8 flex max-w-none w-auto translate-x-0 translate-y-0 flex-col gap-2 p-3 sm:max-w-none"
      >
        <DialogHeader>
          <DialogTitle class="text-sm">{{ title ?? "JSON 查看" }}</DialogTitle>
        </DialogHeader>
        <div class="min-h-0 flex-1">
          <MonacoEditor
            :model-value="text"
            language="json"
            readonly
            height="100%"
            :word-wrap="wrapOn"
            @ready="registerWrapToggle"
          />
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue"
import { Maximize2, TextWrap } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import MonacoEditor from "@/components/workflow-designer/MonacoEditor.vue"
import type { editor as monacoEditor } from "monaco-editor"

/**
 * Read-only JSON viewer for execution details (activity state / outputs /
 * exception). Monaco gives syntax highlighting plus its right-click menu;
 * word wrap defaults on with an explicit toggle button, and because standalone
 * Monaco ships no built-in "toggle word wrap" context-menu entry (that action
 * lives in VS Code workbench, not monaco-editor), the same toggle is injected
 * into each editor's context menu on ready. A maximize button reopens the same
 * content in a near-fullscreen dialog, mirroring CodeLiteralEditor's expand
 * pattern, because the inline slot is too small for long payloads.
 */
const props = withDefaults(
  defineProps<{ value: unknown; title?: string; height?: string }>(),
  { height: "192px" },
)

const text = computed(() => {
  if (props.value == null) return ""
  if (typeof props.value === "string") return props.value
  try { return JSON.stringify(props.value, null, 2) } catch { return String(props.value) }
})

const wrapOn = ref(true)
const expanded = ref(false)

function registerWrapToggle(editor: monacoEditor.IStandaloneCodeEditor): void {
  editor.addAction({
    id: "jsonViewer.toggleWordWrap",
    label: "切换自动换行",
    contextMenuGroupId: "10_wrap",
    contextMenuOrder: 1,
    run: () => { wrapOn.value = !wrapOn.value },
  })
}
</script>
