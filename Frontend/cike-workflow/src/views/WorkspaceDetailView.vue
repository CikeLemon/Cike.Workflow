<template>
  <div class="space-y-4">
    <!-- Workspace Header -->
    <div>
      <h1 class="text-xl font-semibold tracking-tight">
        {{ workspace?.name || "空间" }}
      </h1>
    </div>

    <!-- Tabs -->
    <Tabs :model-value="activeTab()">
      <TabsList>
        <TabsTrigger value="definitions" @click="$router.push({ name: 'definitions', params: { workspaceId } })">
          工作流定义
        </TabsTrigger>
        <TabsTrigger value="instances" @click="$router.push({ name: 'instances', params: { workspaceId } })">
          工作流实例
        </TabsTrigger>
      </TabsList>
    </Tabs>

    <!-- Tab Content -->
    <RouterView />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from "vue"
import { useRoute } from "vue-router"
import { getApiV1Workspaces } from "@/api"
import type { WorkspaceItemDto } from "@/api"
import { setWorkspaceName } from "@/composables/useWorkspaceName"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { RouterView } from "vue-router"

const route = useRoute()
const workspaceId = route.params.workspaceId as string

const workspace = ref<WorkspaceItemDto | null>(null)

onMounted(async () => {
  const { data } = await getApiV1Workspaces({ query: { workspaceId } })
  workspace.value = data ?? null
  // 写入共享状态供面包屑显示空间名称
  if (data?.name) {
    setWorkspaceName(workspaceId, data.name)
  }
})

function activeTab(): string {
  const name = route.name as string
  if (name === "instances" || name === "instance-detail") return "instances"
  return "definitions"
}
</script>
