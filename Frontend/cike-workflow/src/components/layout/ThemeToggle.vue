<template>
  <Button
    variant="ghost"
    size="icon"
    :aria-label="isDark ? '切换到浅色模式' : '切换到深色模式'"
    @click="toggleTheme()"
  >
    <Moon v-if="isDark" />
    <Sun v-else />
  </Button>
</template>

<script setup lang="ts">
import { Sun, Moon } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { useTheme } from "@/composables/useTheme"

const { isDark, toggleTheme } = useTheme()
</script>
