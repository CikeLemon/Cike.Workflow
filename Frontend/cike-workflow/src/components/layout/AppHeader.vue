<template>
  <header class="flex h-14 shrink-0 items-center gap-2 border-b px-4">
    <SidebarTrigger class="-ml-1" />
    <Separator orientation="vertical" class="mr-1 !h-4" />
    <div class="flex-1">
      <AppBreadcrumb />
    </div>
    <ThemeToggle />
  </header>
</template>

<script setup lang="ts">
import { SidebarTrigger } from "@/components/ui/sidebar"
import { Separator } from "@/components/ui/separator"
import AppBreadcrumb from "./AppBreadcrumb.vue"
import ThemeToggle from "./ThemeToggle.vue"
</script>
