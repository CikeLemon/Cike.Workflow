<template>
  <div class="space-y-4">
    <!-- 路径条（非根目录时显示） -->
    <div v-if="!isRootFolder" class="flex items-center gap-1 rounded-md border bg-muted/30 px-3 py-2 text-sm">
      <button
        type="button"
        class="flex items-center gap-1 text-muted-foreground hover:text-foreground"
        @click="navigateToFolder(ROOT_FOLDER_ID)"
      >
        <Home :size="14" />
        根目录
      </button>
      <template v-for="segment in pathSegments" :key="segment.id">
        <ChevronRight :size="14" class="text-muted-foreground" />
        <button
          type="button"
          class="text-muted-foreground hover:text-foreground"
          @click="navigateToFolder(segment.id!)"
        >
          {{ segment.name }}
        </button>
      </template>
      <ChevronRight :size="14" class="text-muted-foreground" />
      <span class="font-medium">{{ folderDetail?.name }}</span>
    </div>

    <!-- 工具栏 -->
    <div class="flex items-center justify-between gap-4">
      <div class="relative max-w-sm flex-1">
        <Search :size="16" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="keyword"
          placeholder="搜索定义或目录..."
          class="pl-9"
          @keyup.enter="handleSearch"
        />
      </div>
      <div class="flex gap-2">
        <Button variant="outline" @click="openCreateFolder">
          <Plus />
          新建目录
        </Button>
        <Button @click="openCreateDefinition">
          <Plus />
          新建定义
        </Button>
      </div>
    </div>

    <!-- 加载状态 -->
    <div v-if="loading" class="text-sm text-muted-foreground">加载中...</div>

    <!-- 加载失败 -->
    <div v-else-if="loadError" class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16">
      <h3 class="font-medium">加载失败</h3>
      <p class="mt-1 text-sm text-muted-foreground">
        无法获取定义列表，请检查后端服务后重试
      </p>
      <Button class="mt-4" variant="outline" @click="refresh">
        重试
      </Button>
    </div>

    <!-- 列表 -->
    <div v-else-if="items.length" class="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow class="bg-muted/50 hover:bg-muted/50">
            <TableHead>名称</TableHead>
            <TableHead>类型</TableHead>
            <TableHead>版本</TableHead>
            <TableHead>发布状态</TableHead>
            <TableHead class="text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-for="item in items" :key="item.id">
            <!-- 目录行 -->
            <TableRow v-if="item.type === 1" class="group">
              <TableCell>
                <button
                  type="button"
                  class="flex items-center gap-2 font-medium hover:text-primary"
                  @click="navigateToFolder(item.id!)"
                >
                  <Folder :size="16" class="text-warning" />
                  {{ getDeleteItemName(item) }}
                </button>
              </TableCell>
              <TableCell class="text-muted-foreground">目录</TableCell>
              <TableCell />
              <TableCell />
              <TableCell class="text-right">
                <div class="flex justify-end gap-1 opacity-0 group-hover:opacity-100">
                  <Button variant="ghost" size="icon-xs" title="重命名" @click="openRenameFolder(item)">
                    <Pencil />
                  </Button>
                  <Button variant="ghost" size="icon-xs" title="删除" class="text-destructive hover:text-destructive" @click="openDelete(item)">
                    <Trash2 />
                  </Button>
                </div>
              </TableCell>
            </TableRow>

            <!-- 定义行 -->
            <TableRow v-else class="group">
              <TableCell>
                <RouterLink
                  :to="`/workspaces/${workspaceId}/definitions/${item.id}`"
                  class="flex items-center gap-2 hover:text-primary"
                >
                  <Workflow :size="16" class="text-muted-foreground" />
                  <span class="font-medium">{{ getDeleteItemName(item) }}</span>
                  <Badge v-if="getDefinitionData(item)?.isSystem" variant="outline" class="text-xs">系统</Badge>
                  <Badge v-if="getDefinitionData(item)?.isReadonly" variant="outline" class="text-xs">只读</Badge>
                </RouterLink>
                <div
                  v-if="getDefinitionData(item)?.definitionId"
                  class="mt-1 flex items-center gap-1 pl-6"
                >
                  <span
                    class="max-w-[240px] truncate font-mono text-xs text-muted-foreground"
                    :title="getDefinitionData(item)?.definitionId"
                  >
                    {{ getDefinitionData(item)?.definitionId }}
                  </span>
                  <button
                    type="button"
                    class="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                    :title="copiedId === getDefinitionData(item)?.definitionId ? '已复制' : '复制定义 ID'"
                    @click="copyDefinitionId(getDefinitionData(item)!.definitionId!)"
                  >
                    <Check v-if="copiedId === getDefinitionData(item)?.definitionId" :size="13" class="text-success" />
                    <Copy v-else :size="13" />
                  </button>
                </div>
              </TableCell>
              <TableCell>
                <Badge :class="typeBadgeClass(getDefinitionData(item)?.type)">
                  {{ typeLabels[getDefinitionData(item)?.type ?? 0] ?? '未知' }}
                </Badge>
              </TableCell>
              <TableCell class="font-mono text-xs">
                v{{ getDefinitionData(item)?.version }}
                <Badge v-if="getDefinitionData(item)?.isLatest" class="ml-1 bg-success/15 text-success border-transparent text-[10px]">最新</Badge>
              </TableCell>
              <TableCell>
                <Badge v-if="getDefinitionData(item)?.publishedVersion" class="bg-success/15 text-success border-transparent">
                  已发布 v{{ getDefinitionData(item)?.publishedVersion }}
                </Badge>
                <span v-else class="text-xs text-muted-foreground">未发布</span>
              </TableCell>
              <TableCell class="text-right">
                <div class="flex justify-end gap-1 opacity-0 group-hover:opacity-100">
                  <Button variant="ghost" size="icon-xs" title="编辑" @click="openEditDefinition(item)">
                    <Pencil />
                  </Button>
                  <Button variant="ghost" size="icon-xs" title="移动" @click="openMoveDefinition(item)">
                    <Move />
                  </Button>
                  <Button variant="ghost" size="icon-xs" title="删除" class="text-destructive hover:text-destructive" @click="openDelete(item)">
                    <Trash2 />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>

    <!-- 空态 -->
    <div v-else class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16">
      <Folder :size="40" class="text-muted-foreground/50" />
      <h3 class="mt-4 font-medium">当前目录为空</h3>
      <p class="mt-1 text-sm text-muted-foreground">
        创建目录或定义来组织你的工作流
      </p>
      <div class="mt-4 flex gap-2">
        <Button variant="outline" @click="openCreateFolder">
          <Plus />
          新建目录
        </Button>
        <Button @click="openCreateDefinition">
          <Plus />
          新建定义
        </Button>
      </div>
    </div>

    <!-- 目录新建/重命名对话框 -->
    <FolderFormDialog
      v-model:open="folderDialogOpen"
      :folder="editingFolder"
      :parent-id="currentFolderId"
      :workspace-id="workspaceId"
      @saved="refresh"
    />

    <!-- 定义新建/编辑对话框 -->
    <DefinitionFormDialog
      v-model:open="definitionDialogOpen"
      :definition="editingDefinition"
      :folder-id="currentFolderId"
      :workspace-id="workspaceId"
      @saved="refresh"
    />

    <!-- 移动定义对话框 -->
    <MoveDefinitionDialog
      v-model:open="moveDialogOpen"
      :definition="movingDefinition"
      :workspace-id="workspaceId"
      :current-folder-id="currentFolderId"
      @moved="refresh"
    />

    <!-- 删除确认对话框 -->
    <AlertDialog v-model:open="deleteDialogOpen">
      <AlertDialogContent class="sm:max-w-md">
        <AlertDialogHeader>
          <AlertDialogTitle>确认删除</AlertDialogTitle>
          <AlertDialogDescription>
            <template v-if="deletingItem?.type === 1">
              确定要删除目录「{{ getDeleteItemName(deletingItem) }}」吗？目录内的定义将被一并删除，此操作不可撤销。
            </template>
            <template v-else>
              确定要删除定义「{{ getDeleteItemName(deletingItem) }}」吗？此操作不可撤销。
            </template>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <p v-if="deleteError" class="text-sm text-destructive">
          {{ deleteError }}
        </p>
        <AlertDialogFooter>
          <Button variant="outline" @click="deleteDialogOpen = false">
            取消
          </Button>
          <Button variant="destructive" :disabled="deleting" @click="confirmDelete">
            {{ deleting ? '删除中...' : '删除' }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from "vue"
import { RouterLink, useRoute, useRouter } from "vue-router"
import { useClipboard } from "@vueuse/core"
import { Folder, Workflow, Plus, Search, Pencil, Trash2, Move, ChevronRight, Home, Copy, Check } from "@lucide/vue"
import {
  getApiV1WorkflowDefinitionsList,
  getApiV1Folders,
  deleteApiV1Folders,
  deleteApiV1WorkflowDefinitionsById,
} from "@/api"
import type {
  WorkflowDefinitionFolderItemDto,
  WorkflowDefinitionItemDto,
  FolderDetailDto,
  FolderPathDto,
} from "@/api"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import FolderFormDialog from "@/components/FolderFormDialog.vue"
import DefinitionFormDialog from "@/components/DefinitionFormDialog.vue"
import MoveDefinitionDialog from "@/components/MoveDefinitionDialog.vue"
import { extractApiErrorMessage } from "@/lib/apiError"

const ROOT_FOLDER_ID = "0"

const route = useRoute()
const router = useRouter()
const workspaceId = route.params.workspaceId as string

// --- 数据层 ---
const items = ref<WorkflowDefinitionFolderItemDto[]>([])
const folderDetail = ref<FolderDetailDto | null>(null)
const loading = ref(false)
const loadError = ref(false)
const keyword = ref("")

const currentFolderId = computed(
  () => (route.query.folderId as string) ?? ROOT_FOLDER_ID,
)

const isRootFolder = computed(() => currentFolderId.value === ROOT_FOLDER_ID)

// Monotonic sequence guard: rapid folder switches / searches fire overlapping fetchList
// calls; only the latest may write `items`, so an older folder's late response can't
// clobber the current folder's list.
let listFetchSeq = 0

async function fetchList() {
  const seq = ++listFetchSeq
  loading.value = true
  loadError.value = false
  try {
    const { data, error } = await getApiV1WorkflowDefinitionsList({
      query: {
        workspaceId,
        folderId: currentFolderId.value,
        keyword: keyword.value || undefined,
      },
    })
    if (seq !== listFetchSeq) return // stale — a newer fetchList owns the state
    if (error) {
      loadError.value = true
      return
    }
    items.value = data ?? []
  } finally {
    if (seq === listFetchSeq) loading.value = false
  }
}

async function fetchFolderDetail() {
  if (isRootFolder.value) {
    folderDetail.value = null
    return
  }
  const { data, error } = await getApiV1Folders({
    query: { folderId: currentFolderId.value },
  })
  if (!error && data) {
    folderDetail.value = data
  }
}

async function refresh() {
  await Promise.all([fetchList(), fetchFolderDetail()])
}

// 监听 folderId 变化重新加载
watch(() => route.query.folderId, refresh, { immediate: true })

function handleSearch() {
  refresh()
}

// --- 目录导航 ---
function navigateToFolder(folderId: string) {
  router.push({
    name: "definitions",
    query: folderId === ROOT_FOLDER_ID ? {} : { folderId },
  })
}

// 路径条：根目录 + path segments + 当前目录名
const pathSegments = computed<FolderPathDto[]>(() => {
  if (!folderDetail.value) return []
  return folderDetail.value.path ?? []
})

// --- 类型映射 ---
const typeLabels: Record<number, string> = {
  1: "Workflow",
  2: "AgentWorkflow",
  3: "Approval",
}

function typeBadgeClass(type: number | undefined): string {
  switch (type) {
    case 1: return "bg-secondary text-secondary-foreground"
    case 2: return "bg-info/15 text-info"
    case 3: return "bg-warning/15 text-warning"
    default: return "bg-muted text-muted-foreground"
  }
}

function getDefinitionData(item: WorkflowDefinitionFolderItemDto): WorkflowDefinitionItemDto | null {
  if (item.type === 2 && item.data) return item.data as WorkflowDefinitionItemDto
  return null
}

// --- 新建/重命名目录 ---
const folderDialogOpen = ref(false)
const editingFolder = ref<{ id: string; name: string } | null>(null)

function openCreateFolder() {
  editingFolder.value = null
  folderDialogOpen.value = true
}

function openRenameFolder(item: WorkflowDefinitionFolderItemDto) {
  const data = item.data as any
  editingFolder.value = { id: item.id!, name: data?.name ?? "" }
  folderDialogOpen.value = true
}

// --- 新建/编辑定义 ---
const definitionDialogOpen = ref(false)
const editingDefinition = ref<WorkflowDefinitionFolderItemDto | null>(null)

function openCreateDefinition() {
  editingDefinition.value = null
  definitionDialogOpen.value = true
}

function openEditDefinition(item: WorkflowDefinitionFolderItemDto) {
  editingDefinition.value = item
  definitionDialogOpen.value = true
}

// --- 移动定义 ---
const moveDialogOpen = ref(false)
const movingDefinition = ref<WorkflowDefinitionFolderItemDto | null>(null)

function openMoveDefinition(item: WorkflowDefinitionFolderItemDto) {
  movingDefinition.value = item
  moveDialogOpen.value = true
}

// --- 删除确认 ---
const deleteDialogOpen = ref(false)
const deletingItem = ref<WorkflowDefinitionFolderItemDto | null>(null)
const deleting = ref(false)
const deleteError = ref("")

function openDelete(item: WorkflowDefinitionFolderItemDto) {
  deletingItem.value = item
  deleteError.value = ""
  deleteDialogOpen.value = true
}

async function confirmDelete() {
  if (!deletingItem.value) return
  deleting.value = true
  deleteError.value = ""
  try {
    const isFolder = deletingItem.value.type === 1
    const { error } = isFolder
      ? await deleteApiV1Folders({ query: { folderId: deletingItem.value.id! } })
      : await deleteApiV1WorkflowDefinitionsById({ path: { id: deletingItem.value.id! } })
    if (error) {
      deleteError.value = extractApiErrorMessage(error, "删除失败，请稍后重试")
      return
    }
    deleteDialogOpen.value = false
    deletingItem.value = null
    refresh()
  } finally {
    deleting.value = false
  }
}

function getDeleteItemName(item: WorkflowDefinitionFolderItemDto | null): string {
  if (!item || !item.data || !('name' in item.data)) return ""
  return (item.data as any).name ?? ""
}

// --- 复制定义 ID ---
const { copy } = useClipboard()
const copiedId = ref("")

async function copyDefinitionId(definitionId: string) {
  if (!definitionId) return
  await copy(definitionId)
  copiedId.value = definitionId
  setTimeout(() => {
    if (copiedId.value === definitionId) copiedId.value = ""
  }, 1500)
}
</script>
