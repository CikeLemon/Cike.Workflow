<template>
  <Teleport to="body">
    <div class="fixed inset-0 z-40" @click="emit('close')" @contextmenu.prevent="emit('close')" />
    <div
      ref="menuRef"
      class="fixed z-50 w-56 overflow-hidden rounded-md border bg-popover text-popover-foreground shadow-md"
      :style="style"
    >
      <div class="max-h-72 overflow-y-auto py-1">
        <div v-for="group in groups" :key="group.category">
          <div class="px-3 pb-1 pt-2 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
            {{ group.category }}
          </div>
          <button
            v-for="item in group.items"
            :key="item.typeName"
            type="button"
            class="flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent"
            :title="item.description ?? item.typeName"
            @click="emit('select', item.typeName)"
          >
            <component :is="resolveActivityIcon(item.icon)" :size="15" class="shrink-0 text-muted-foreground" />
            <span class="truncate text-xs font-medium">{{ item.displayName }}</span>
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue"
import type { PaletteGroup } from "@/core/designer/palette"
import { resolveActivityIcon } from "./nodes/icons"

const props = defineProps<{
  /** Connection-insertable activity groups (Start/End already filtered out). */
  groups: PaletteGroup[]
  /** Client coordinates of the edge insert button that opened this menu. */
  x: number
  y: number
}>()

const emit = defineEmits<{
  select: [typeName: string]
  close: []
}>()

const menuRef = ref<HTMLDivElement>()
const style = ref<Record<string, string>>({ visibility: "hidden" })

/** Anchor below the button, flipping above / clamping to stay in the viewport. */
function place(): void {
  const el = menuRef.value
  if (!el) return
  const { offsetWidth: width, offsetHeight: height } = el
  const left = Math.min(Math.max(8, props.x - width / 2), window.innerWidth - width - 8)
  let top = props.y + 14
  if (top + height > window.innerHeight - 8) top = Math.max(8, props.y - 14 - height)
  style.value = { left: `${left}px`, top: `${top}px` }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === "Escape") emit("close")
}

onMounted(() => {
  place()
  window.addEventListener("keydown", onKeydown)
})

onBeforeUnmount(() => window.removeEventListener("keydown", onKeydown))
</script>
