<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>移动到...</DialogTitle>
        <DialogDescription>
          选择目标目录，将「{{ definition?.data && 'name' in definition.data ? (definition.data as any).name : '' }}」移动到该位置
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-3">
        <!-- 路径条 -->
        <div class="flex items-center gap-1 rounded-md border bg-muted/30 px-3 py-2 text-sm">
          <button
            type="button"
            class="flex items-center gap-1 text-muted-foreground hover:text-foreground"
            @click="goToRoot"
          >
            <Home :size="14" />
            根目录
          </button>
          <template v-for="segment in pickerPath" :key="segment.id">
            <ChevronRight :size="14" class="text-muted-foreground" />
            <button
              type="button"
              class="text-muted-foreground hover:text-foreground"
              @click="goToFolder(segment)"
            >
              {{ segment.name }}
            </button>
          </template>
          <template v-if="pickerFolderName">
            <ChevronRight :size="14" class="text-muted-foreground" />
            <span class="font-medium">{{ pickerFolderName }}</span>
          </template>
        </div>

        <!-- 目录列表 -->
        <div class="min-h-[200px] max-h-[300px] overflow-y-auto rounded-md border">
          <div v-if="loading" class="flex items-center justify-center py-8 text-sm text-muted-foreground">
            加载中...
          </div>
          <div v-else-if="folders.length === 0" class="flex flex-col items-center justify-center py-8 text-sm text-muted-foreground">
            <Folder :size="24" class="mb-2 text-muted-foreground/50" />
            当前目录下没有子目录
          </div>
          <div v-else class="divide-y">
            <button
              v-for="folder in folders"
              :key="folder.id"
              type="button"
              class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
              @click="enterFolder(folder)"
            >
              <Folder :size="16" class="text-warning" />
              <span>{{ (folder.data as any)?.name ?? '未命名' }}</span>
              <ChevronRight :size="14" class="ml-auto text-muted-foreground" />
            </button>
          </div>
        </div>

        <p v-if="errorMessage" class="text-sm text-destructive">
          {{ errorMessage }}
        </p>
      </div>

      <DialogFooter>
        <Button type="button" variant="outline" @click="close">
          取消
        </Button>
        <Button :disabled="moving || !canMoveHere()" @click="handleMove">
          {{ moving ? '移动中...' : '移动到此处' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { ref, watch } from "vue"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { ChevronRight, Folder, Home } from "@lucide/vue"
import {
  getApiV1WorkflowDefinitionsList,
  getApiV1Folders,
  postApiV1WorkflowDefinitionsMoveById,
} from "@/api"
import type { WorkflowDefinitionFolderItemDto, FolderPathDto } from "@/api"
import { extractApiErrorMessage } from "@/lib/apiError"

const ROOT_FOLDER_ID = "0"

const props = defineProps<{
  open: boolean
  definition: WorkflowDefinitionFolderItemDto | null
  workspaceId: string
  currentFolderId: string
}>()

const emit = defineEmits<{
  (e: "update:open", value: boolean): void
  (e: "moved"): void
}>()

const pickerFolderId = ref(ROOT_FOLDER_ID)
const pickerFolderName = ref("")
const pickerPath = ref<FolderPathDto[]>([])
const folders = ref<WorkflowDefinitionFolderItemDto[]>([])
const loading = ref(false)
const moving = ref(false)
const errorMessage = ref("")

function close() {
  emit("update:open", false)
}

async function loadFolder(folderId: string) {
  loading.value = true
  errorMessage.value = ""
  try {
    // 加载当前目录下的内容，过滤出目录
    const { data, error } = await getApiV1WorkflowDefinitionsList({
      query: { workspaceId: props.workspaceId, folderId },
    })
    if (error) {
      errorMessage.value = "加载目录失败"
      return
    }
    folders.value = (data ?? []).filter((item) => item.type === 1)
    pickerFolderId.value = folderId

    // 加载路径信息
    if (folderId === ROOT_FOLDER_ID) {
      pickerPath.value = []
      pickerFolderName.value = ""
    } else {
      const { data: detail, error: detailError } = await getApiV1Folders({
        query: { folderId },
      })
      if (!detailError && detail) {
        pickerPath.value = detail.path ?? []
        pickerFolderName.value = detail.name ?? ""
      }
    }
  } finally {
    loading.value = false
  }
}

watch(
  () => props.open,
  (val) => {
    if (val) {
      errorMessage.value = ""
      // 从根目录开始浏览
      loadFolder(ROOT_FOLDER_ID)
    }
  },
)

function enterFolder(folder: WorkflowDefinitionFolderItemDto) {
  loadFolder(folder.id!)
}

function goToRoot() {
  loadFolder(ROOT_FOLDER_ID)
}

function goToFolder(path: FolderPathDto) {
  loadFolder(path.id!)
}

const canMoveHere = () => {
  // 不能移动到定义当前所在目录
  return pickerFolderId.value !== props.currentFolderId
}

async function handleMove() {
  if (!props.definition || !canMoveHere()) return

  moving.value = true
  errorMessage.value = ""
  try {
    const { error } = await postApiV1WorkflowDefinitionsMoveById({
      path: { id: props.definition.id! },
      body: { folderId: pickerFolderId.value },
    })
    if (error) {
      errorMessage.value = extractApiErrorMessage(error, "移动失败，请稍后重试")
      return
    }
    close()
    emit("moved")
  } finally {
    moving.value = false
  }
}
</script>
