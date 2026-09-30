<script setup lang="ts">
import { ref } from "vue"
import { History } from "@lucide/vue"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"

const props = defineProps<{ designer: WorkflowDesignerState; open: boolean }>()

const emit = defineEmits<{ "update:open": [open: boolean] }>()

const rollbackTargetId = ref<string | null>(null)
const rollbackConfirmOpen = ref(false)

function formatDate(value?: string): string {
  if (!value) return "—"
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleDateString()
}

async function view(rowId?: string) {
  if (!rowId) return
  await props.designer.viewVersion(rowId)
  emit("update:open", false)
}

function requestRollback(rowId?: string) {
  if (!rowId) return
  rollbackTargetId.value = rowId
  rollbackConfirmOpen.value = true
}

async function confirmRollback() {
  const target = rollbackTargetId.value
  rollbackTargetId.value = null
  if (!target) return
  await props.designer.rollback(target)
  emit("update:open", false)
}
</script>

<template>
  <Sheet :open="open" @update:open="(value: boolean) => emit('update:open', value)">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader>
        <SheetTitle class="flex items-center gap-2">
          <History :size="16" />
          版本历史
        </SheetTitle>
        <SheetDescription>查看历史版本内容或回滚到指定版本。</SheetDescription>
      </SheetHeader>

      <div class="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
        <ul class="divide-y">
          <li
            v-for="v in designer.versions.value"
            :key="v.id"
            class="flex items-start justify-between gap-3 py-3"
          >
            <div class="min-w-0">
              <div class="flex items-center gap-1.5">
                <span class="font-mono text-sm">v{{ v.version }}</span>
                <Badge v-if="v.isLatest" class="bg-success/15 text-success border-transparent">最新</Badge>
                <Badge v-if="v.isPublished" class="bg-info/15 text-info border-transparent">已发布</Badge>
                <Badge
                  v-if="String(v.id) === designer.rowId.value"
                  variant="secondary"
                >
                  当前查看
                </Badge>
              </div>
              <p class="mt-1 truncate text-xs text-muted-foreground">
                {{ v.publishedNote || "无备注" }}
              </p>
              <p class="mt-0.5 text-xs text-muted-foreground">{{ formatDate(v.publishedAt) }}</p>
            </div>
            <div class="flex shrink-0 flex-col items-end gap-1">
              <Button
                variant="ghost"
                size="sm"
                :disabled="String(v.id) === designer.rowId.value"
                @click="view(v.id)"
              >
                查看
              </Button>
              <Button
                v-if="!v.isLatest"
                variant="ghost"
                size="sm"
                @click="requestRollback(v.id)"
              >
                回滚到此版本
              </Button>
            </div>
          </li>
        </ul>
        <p v-if="designer.versionsError.value" class="py-6 text-center text-sm text-destructive">
          {{ designer.versionsError.value }}
        </p>
        <p v-else-if="designer.versions.value.length === 0" class="py-6 text-center text-sm text-muted-foreground">
          暂无版本记录
        </p>
      </div>
    </SheetContent>
  </Sheet>

  <AlertDialog :open="rollbackConfirmOpen" @update:open="(value: boolean) => (rollbackConfirmOpen = value)">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>回滚版本</AlertDialogTitle>
        <AlertDialogDescription>
          将以该版本的内容创建新的最新版本，当前草稿的未保存改动会丢失。确认回滚？
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>取消</AlertDialogCancel>
        <AlertDialogAction @click="confirmRollback">回滚</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
