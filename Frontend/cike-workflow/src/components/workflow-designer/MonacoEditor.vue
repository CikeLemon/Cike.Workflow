<template>
  <div ref="container" class="w-full overflow-hidden rounded-md border" :style="{ height }" />
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from "vue"

/**
 * Thin Monaco wrapper. Monaco is heavy (~500KB) so it is imported lazily on
 * first mount and never lands in the initial bundle (ADR 0004: JavaScript /
 * Liquid expressions use Monaco; code-like Literal slots, e.g. RunJavaScript's
 * script, embed it too while plain-value Literals stay on form controls).
 */
const props = withDefaults(
  defineProps<{
    modelValue: string
    language?: string
    readonly?: boolean
    height?: string
    wordWrap?: boolean
  }>(),
  {
    language: "plaintext",
    readonly: false,
    height: "120px",
    wordWrap: false,
  },
)

const emit = defineEmits<{
  (e: "update:modelValue", value: string): void
  (e: "blur"): void
  (e: "ready", editor: import("monaco-editor").editor.IStandaloneCodeEditor): void
}>()

const container = ref<HTMLDivElement | null>(null)
const monacoRef = shallowRef<typeof import("monaco-editor") | null>(null)
const editorRef = shallowRef<import("monaco-editor").editor.IStandaloneCodeEditor | null>(null)
let disposed = false
let observer: MutationObserver | null = null

function currentTheme(): string {
  return document.documentElement.classList.contains("dark") ? "vs-dark" : "vs"
}

async function setup(): Promise<void> {
  if (!container.value || disposed) return
  const [monaco, editorWorker, tsWorker, jsonWorker] = await Promise.all([
    import("monaco-editor"),
    import("monaco-editor/editor/editor.worker?worker"),
    import("monaco-editor/languages/features/typescript/ts.worker?worker"),
    import("monaco-editor/languages/features/json/json.worker?worker"),
  ])
  if (disposed || !container.value) return
  // Monaco requests workers by language label; the JavaScript/TypeScript language
  // service needs ts.worker (diagnostics + completions), JSON needs json.worker
  // (schema validation + diagnostics), everything else uses the base editor
  // worker. Routing by label avoids unhandled rejections from language features
  // hitting a worker that does not implement them.
  ;(self as unknown as { MonacoEnvironment: unknown }).MonacoEnvironment = {
    getWorker: (_workerId: string, label: string) => {
      if (label === "typescript" || label === "javascript") return new tsWorker.default()
      if (label === "json") return new jsonWorker.default()
      return new editorWorker.default()
    },
  }
  const editor = monaco.editor.create(container.value, {
    value: props.modelValue ?? "",
    language: props.language,
    theme: currentTheme(),
    readOnly: props.readonly,
    wordWrap: props.wordWrap ? "on" : "off",
    automaticLayout: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    fontSize: 12,
    lineNumbers: "off",
    tabSize: 2,
    renderLineHighlight: "none",
    overviewRulerLanes: 0,
    hideCursorInOverviewRuler: true,
    scrollbar: { verticalScrollbarSize: 8, horizontalScrollbarSize: 8 },
    // Suggest/hover widgets are laid out as fixed-position overlays in viewport
    // coordinates, so they escape the small overflow-hidden container instead
    // of being clipped by it (the panel chain must stay transform-free).
    fixedOverflowWidgets: true,
  })
  editor.onDidChangeModelContent(() => {
    emit("update:modelValue", editor.getValue())
  })
  editor.onDidBlurEditorText(() => emit("blur"))
  monacoRef.value = monaco
  editorRef.value = editor
  emit("ready", editor)
  // Follow the app light/dark theme live.
  observer = new MutationObserver(() => monaco.editor.setTheme(currentTheme()))
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] })
}

onMounted(() => {
  void setup()
})

onBeforeUnmount(() => {
  disposed = true
  observer?.disconnect()
  observer = null
  editorRef.value?.dispose()
  editorRef.value = null
  monacoRef.value = null
})

// External model changes (undo/redo, type switch): only push when different so
// the cursor is not clobbered during normal typing.
watch(
  () => props.modelValue,
  (next) => {
    const editor = editorRef.value
    if (!editor) return
    if (editor.getValue() !== (next ?? "")) editor.setValue(next ?? "")
  },
)

watch(
  () => props.language,
  (lang) => {
    const editor = editorRef.value
    const monaco = monacoRef.value
    const model = editor?.getModel()
    if (editor && monaco && model) monaco.editor.setModelLanguage(model, lang)
  },
)

watch(
  () => props.readonly,
  (ro) => editorRef.value?.updateOptions({ readOnly: ro }),
)

watch(
  () => props.wordWrap,
  (wrap) => editorRef.value?.updateOptions({ wordWrap: wrap ? "on" : "off" }),
)
</script>
