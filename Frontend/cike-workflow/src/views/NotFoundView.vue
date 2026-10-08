<template>
  <div class="flex flex-col items-center justify-center py-24">
    <AlertTriangle :size="48" class="text-muted-foreground/50" />
    <h1 class="mt-4 text-2xl font-semibold">页面未找到</h1>
    <p class="mt-2 text-sm text-muted-foreground">
      你访问的页面不存在或已被移除
    </p>
    <RouterLink
      to="/"
      class="mt-6 inline-flex h-9 items-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90"
    >
      返回首页
    </RouterLink>
  </div>
</template>

<script setup lang="ts">
import { RouterLink } from "vue-router"
import { AlertTriangle } from "@lucide/vue"
</script>
