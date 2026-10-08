<template>
  <nav class="flex min-w-0 items-center gap-1 text-sm">
    <template v-for="(entry, index) in entries" :key="index">
      <ChevronRight v-if="index > 0" :size="14" class="shrink-0 text-muted-foreground" />
      <button
        v-if="index < entries.length - 1"
        class="max-w-48 shrink truncate rounded px-1 py-0.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        @click="emit('select', index)"
      >
        {{ entry }}
      </button>
      <span v-else class="max-w-48 shrink truncate rounded bg-secondary px-1.5 py-0.5 font-medium text-foreground">
        {{ entry }}
      </span>
    </template>
  </nav>
</template>

<script setup lang="ts">
import { ChevronRight } from "@lucide/vue"

defineProps<{ entries: string[] }>()

const emit = defineEmits<{ select: [index: number] }>()
</script>
