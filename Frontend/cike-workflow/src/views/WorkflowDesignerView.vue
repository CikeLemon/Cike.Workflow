<template>
  <div class="h-full min-h-0">
    <div v-if="designer.loading.value" class="flex h-full items-center justify-center text-sm text-muted-foreground">
      加载中…
    </div>
    <WorkflowDesigner v-else :designer="designer" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, watch } from "vue"
import { useRoute, useRouter } from "vue-router"
import { useWorkflowDesigner } from "@/composables/useWorkflowDesigner"
import WorkflowDesigner from "@/components/workflow-designer/WorkflowDesigner.vue"

const route = useRoute()
const router = useRouter()
const designer = useWorkflowDesigner()

onMounted(() => {
  designer.load(route.params.definitionId as string)
})

// Saving a published version forks a new draft version row; the composable
// reloads onto the new row id. Keep the URL in step so a refresh/share lands
// on the version actually being edited.
watch(
  () => designer.rowId.value,
  (rowId) => {
    if (rowId && rowId !== route.params.definitionId) {
      router.replace({
        name: "definition-detail",
        params: { ...route.params, definitionId: rowId },
      })
    }
  },
)
</script>
