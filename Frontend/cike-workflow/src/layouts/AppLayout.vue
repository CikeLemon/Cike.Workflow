<template>
  <SidebarProvider class="h-svh overflow-hidden">
    <AppSidebar />
    <SidebarInset class="overflow-hidden">
      <AppHeader />
      <div
        id="main-content"
        tabindex="-1"
        class="flex-1 overflow-y-auto overscroll-contain p-6 outline-none"
      >
        <slot />
      </div>
    </SidebarInset>
  </SidebarProvider>
</template>

<script setup lang="ts">
import { SidebarProvider, SidebarInset } from "@/components/ui/sidebar"
import AppSidebar from "@/components/layout/AppSidebar.vue"
import AppHeader from "@/components/layout/AppHeader.vue"
</script>
