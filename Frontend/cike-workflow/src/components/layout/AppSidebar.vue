<template>
  <Sidebar collapsible="icon">
    <SidebarHeader>
      <RouterLink to="/" class="flex items-center gap-2 px-1 py-1.5 font-semibold">
        <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-primary text-xs font-bold text-primary-foreground">
          CW
        </div>
        <span class="text-sm group-data-[collapsible=icon]:hidden">Cike Workflow</span>
      </RouterLink>
    </SidebarHeader>

    <SidebarContent>
      <SidebarGroup>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem v-for="item in menuItems" :key="item.name">
              <SidebarMenuButton
                as-child
                :is-active="isActive(item.name)"
                :tooltip="item.label"
              >
                <RouterLink :to="item.to">
                  <component :is="item.icon" />
                  <span>{{ item.label }}</span>
                </RouterLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>
    </SidebarContent>

    <SidebarRail />
  </Sidebar>
</template>

<script setup lang="ts">
import { RouterLink, useRoute } from "vue-router"
import { LayoutDashboard, Boxes } from "@lucide/vue"
import {
  Sidebar,
  SidebarHeader,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
  SidebarRail,
} from "@/components/ui/sidebar"

const route = useRoute()

const menuItems = [
  { label: "首页", to: "/", icon: LayoutDashboard, name: "home" },
  { label: "空间管理", to: "/workspaces", icon: Boxes, name: "workspaces" },
] as const

function isActive(name: string) {
  if (name === "home") return route.name === "home"
  // "workspaces" matches all workspace-related routes
  return route.path.startsWith("/workspaces") || route.name === name
}
</script>
