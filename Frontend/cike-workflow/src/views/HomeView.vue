<template>
  <div class="space-y-8">
    <!-- Welcome -->
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">欢迎使用 Cike Workflow</h1>
      <p class="mt-1 text-sm text-muted-foreground">
        工作流管理与自动化平台
      </p>
    </div>

    <!-- Quick Actions -->
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <RouterLink
        to="/workspaces"
        class="group rounded-lg border p-6 transition-colors hover:border-primary/50 hover:bg-accent/50"
      >
        <div class="flex items-center gap-3">
          <div class="flex h-10 w-10 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Boxes :size="20" />
          </div>
          <div>
            <h3 class="font-medium">空间管理</h3>
            <p class="text-xs text-muted-foreground">创建和管理你的工作空间</p>
          </div>
        </div>
      </RouterLink>

      <div class="rounded-lg border p-6 opacity-60">
        <div class="flex items-center gap-3">
          <div class="flex h-10 w-10 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <LayoutDashboard :size="20" />
          </div>
          <div>
            <h3 class="font-medium">报表概览</h3>
            <p class="text-xs text-muted-foreground">即将上线</p>
          </div>
        </div>
      </div>

      <div class="rounded-lg border p-6 opacity-60">
        <div class="flex items-center gap-3">
          <div class="flex h-10 w-10 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <Sparkles :size="20" />
          </div>
          <div>
            <h3 class="font-medium">新手引导</h3>
            <p class="text-xs text-muted-foreground">即将上线</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Documentation -->
    <div class="rounded-lg border p-6">
      <div class="flex items-center gap-2">
        <BookOpen :size="18" class="text-muted-foreground" />
        <h2 class="font-medium">使用文档</h2>
      </div>
      <p class="mt-2 text-sm text-muted-foreground">
        查阅文档了解如何创建工作空间、定义工作流以及监控实例运行状态。
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { LayoutDashboard, Boxes, BookOpen, Sparkles } from "@lucide/vue"
</script>
