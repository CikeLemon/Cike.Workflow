<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue"
import { RouterLink, useRoute } from "vue-router"
import { RefreshCw, Inbox, TriangleAlert, ChevronLeft, ChevronRight, ChevronDown, Search, SearchX } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Switch } from "@/components/ui/switch"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
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
import { canCancel, useInstancesList } from "@/composables/useInstancesList"
import type { WorkflowStatus, WorkflowInstanceItemDto } from "@/api/generated"

const route = useRoute()
const workspaceId = route.params.workspaceId as string

const list = useInstancesList(workspaceId)

// WorkflowStatus is a numeric enum: Pending=0 Executing=1 Suspended=2
// Finished=3 Cancelled=4 Faulted=5 Interrupted=6.
const STATUS_CONFIG: Record<number, { label: string; class: string; dot: string }> = {
  0: { label: "等待中", class: "bg-muted text-muted-foreground", dot: "bg-muted-foreground" },
  1: { label: "执行中", class: "bg-info/15 text-info", dot: "bg-info" },
  2: { label: "已挂起", class: "bg-warning/15 text-warning", dot: "bg-warning" },
  3: { label: "已完成", class: "bg-success/15 text-success", dot: "bg-success" },
  4: { label: "已取消", class: "bg-muted text-muted-foreground", dot: "bg-muted-foreground" },
  5: { label: "已故障", class: "bg-destructive/15 text-destructive", dot: "bg-destructive" },
  6: { label: "已中断", class: "bg-warning/15 text-warning", dot: "bg-warning" },
}
const STATUS_OPTIONS = (Object.keys(STATUS_CONFIG) as unknown as WorkflowStatus[])
  .map(Number)
  .map((v) => ({ value: v as WorkflowStatus, label: STATUS_CONFIG[v]!.label, dot: STATUS_CONFIG[v]!.dot }))

function statusLabel(status?: WorkflowStatus): string {
  return status != null ? (STATUS_CONFIG[status]?.label ?? String(status)) : "—"
}
function statusClass(status?: WorkflowStatus): string {
  return status != null ? (STATUS_CONFIG[status]?.class ?? "bg-muted text-muted-foreground") : "bg-muted text-muted-foreground"
}

function formatDateTime(value?: string): string {
  if (!value) return "—"
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return "—"
  // .NET DateTime.MinValue (0001-01-01) is the backend sentinel for "not finished".
  if (d.getFullYear() <= 1) return "—"
  return d.toLocaleString("zh-CN", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" })
}

function isSubWorkflow(parentId?: string): boolean {
  return !!parentId && parentId !== "0"
}

// --- search: local buffer, committed on Enter (so typing alone doesn't flip filter state) ---
const searchInput = ref("")
function commitSearch(): void {
  list.searchTerm.value = searchInput.value
  void list.applyFilters()
}
watch(() => list.searchTerm.value, (v) => { searchInput.value = v })

// --- definition combobox ---
const defOpen = ref(false)
const selectedDefinitionLabel = computed(() => {
  const id = list.selectedDefinitionIds.value[0]
  if (!id) return ""
  return list.definitionOptions.value.find((o) => o.definitionId === id)?.name ?? id
})
function selectDefinition(id: string): void {
  if (!id) return
  const current = list.selectedDefinitionIds.value
  list.setDefinitionFilter(current.includes(id) ? [] : [id])
  defOpen.value = false
  void list.applyFilters()
}

// --- status chips (multi-select) ---
const statusModel = computed<(string | number)[]>({
  get: () => [...list.selectedStatuses.value],
  set: (v) => {
    list.selectedStatuses.value = v as WorkflowStatus[]
    void list.applyFilters()
  },
})

// --- toggles ---
const hasIncidentsModel = computed({
  get: () => list.hasIncidents.value,
  set: (v: boolean) => {
    list.hasIncidents.value = v
    void list.applyFilters()
  },
})
const isDebugModel = computed({
  get: () => list.isDebug.value,
  set: (v: boolean) => {
    list.isDebug.value = v
    void list.applyFilters()
  },
})

// --- row cancel (irreversible → confirm gate; async → refetch, no optimistic flip) ---
const cancelTarget = ref<WorkflowInstanceItemDto | null>(null)
const cancelOpen = computed({
  get: () => cancelTarget.value !== null,
  set: (v: boolean) => {
    if (!v) cancelTarget.value = null
  },
})
function askCancel(inst: WorkflowInstanceItemDto): void {
  cancelTarget.value = inst
}
async function confirmCancel(): Promise<void> {
  const id = cancelTarget.value?.id
  cancelTarget.value = null
  if (id) await list.cancel(id)
}

onMounted(() => {
  void list.init()
})
</script>

<template>
  <div class="space-y-4">
    <!-- Toolbar: search + definition combobox + refresh -->
    <div class="flex flex-wrap items-center gap-3">
      <div class="relative w-full max-w-sm">
        <Search :size="16" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="searchInput"
          placeholder="搜索实例名称、关联 ID 或实例 ID…"
          class="pl-9"
          @keyup.enter="commitSearch"
        />
      </div>

      <Popover v-model:open="defOpen">
        <PopoverTrigger as-child>
          <Button variant="outline" role="combobox" class="w-56 justify-between font-normal">
            <span :class="selectedDefinitionLabel ? '' : 'text-muted-foreground'">
              {{ selectedDefinitionLabel || "全部工作流定义" }}
            </span>
            <ChevronDown :size="16" class="shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent class="w-56 p-0" align="start">
          <Command>
            <CommandInput placeholder="搜索定义…" />
            <CommandList>
              <CommandEmpty>无匹配定义</CommandEmpty>
              <CommandGroup>
                <CommandItem
                  v-for="opt in list.definitionOptions.value"
                  :key="opt.definitionId"
                  :value="opt.definitionId ?? ''"
                  @select="() => selectDefinition(opt.definitionId!)"
                >
                  {{ opt.name }}
                </CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      <div class="ml-auto flex items-center gap-3">
        <span class="text-sm text-muted-foreground">共 {{ list.total.value }} 个</span>
        <Button variant="outline" size="sm" :disabled="list.loading.value" @click="list.refresh()">
          <RefreshCw :size="16" :class="list.loading.value ? 'animate-spin' : ''" />
          刷新
        </Button>
      </div>
    </div>

    <!-- Toolbar: status chips + toggles -->
    <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
      <ToggleGroup v-model="statusModel" type="multiple" variant="outline" size="sm">
        <ToggleGroupItem v-for="s in STATUS_OPTIONS" :key="s.value" :value="s.value" class="gap-1.5">
          <span class="size-2 rounded-full" :class="s.dot" />
          {{ s.label }}
        </ToggleGroupItem>
      </ToggleGroup>

      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <Switch v-model="hasIncidentsModel" />
        只看有异常
      </label>
      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <Switch v-model="isDebugModel" />
        显示调试实例
      </label>
    </div>

    <!-- Action feedback banner -->
    <div
      v-if="list.actionError.value"
      class="flex items-center justify-between rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive"
    >
      <span>{{ list.actionError.value }}</span>
      <Button variant="ghost" size="sm" class="h-6 text-destructive" @click="list.actionError.value = null">关闭</Button>
    </div>

    <!-- Error state -->
    <div
      v-if="list.loadError.value"
      class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16"
    >
      <TriangleAlert :size="40" class="text-destructive/60" />
      <h3 class="mt-4 font-medium">加载失败</h3>
      <p class="mt-1 text-sm text-muted-foreground">{{ list.loadError.value }}</p>
      <Button class="mt-4" variant="outline" @click="list.refresh()">重试</Button>
    </div>

    <!-- Loading skeleton -->
    <div v-else-if="list.loading.value" class="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow class="bg-muted/50 hover:bg-muted/50">
            <TableHead>状态</TableHead>
            <TableHead>名称 / 定义</TableHead>
            <TableHead>版本</TableHead>
            <TableHead>关联 ID</TableHead>
            <TableHead>异常</TableHead>
            <TableHead>创建时间</TableHead>
            <TableHead>完成时间</TableHead>
            <TableHead>操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="n in 6" :key="n">
            <TableCell v-for="c in 8" :key="c">
              <Skeleton class="h-4 w-full" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <!-- Empty: no instances at all -->
    <div
      v-else-if="list.isEmpty.value"
      class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16"
    >
      <Inbox :size="40" class="text-muted-foreground/50" />
      <h3 class="mt-4 font-medium">暂无工作流实例</h3>
      <p class="mt-1 text-sm text-muted-foreground">运行或调试工作流后，实例会出现在这里</p>
    </div>

    <!-- Empty: filters matched nothing -->
    <div
      v-else-if="list.isFilteredEmpty.value"
      class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16"
    >
      <SearchX :size="40" class="text-muted-foreground/50" />
      <h3 class="mt-4 font-medium">没有符合筛选条件的实例</h3>
      <p class="mt-1 text-sm text-muted-foreground">试着调整或清除筛选条件</p>
      <Button class="mt-4" variant="outline" @click="list.clearFilters()">清除筛选</Button>
    </div>

    <!-- Instance table -->
    <template v-else>
      <div class="rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow class="bg-muted/50 hover:bg-muted/50">
              <TableHead>状态</TableHead>
              <TableHead>名称 / 定义</TableHead>
              <TableHead>版本</TableHead>
              <TableHead>关联 ID</TableHead>
              <TableHead>异常</TableHead>
              <TableHead>创建时间</TableHead>
              <TableHead>完成时间</TableHead>
              <TableHead>操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="inst in list.items.value" :key="inst.id">
              <TableCell>
                <div class="flex items-center gap-1.5">
                  <Badge :class="statusClass(inst.status)">{{ statusLabel(inst.status) }}</Badge>
                  <Badge v-if="inst.isDebug" variant="outline" class="text-xs">调试</Badge>
                </div>
              </TableCell>
              <TableCell>
                <div class="flex items-center gap-1.5">
                  <RouterLink
                    :to="`/workspaces/${workspaceId}/instances/${inst.id}`"
                    class="font-medium hover:text-primary"
                  >
                    {{ inst.name || inst.definitionName || `实例 ${inst.id}` }}
                  </RouterLink>
                  <Badge v-if="isSubWorkflow(inst.parentWorkflowInstanceId)" variant="outline" class="text-xs">
                    子流程
                  </Badge>
                </div>
                <button
                  v-if="inst.definitionId"
                  type="button"
                  class="text-xs text-muted-foreground hover:text-primary hover:underline"
                  @click="selectDefinition(inst.definitionId)"
                >
                  {{ inst.definitionName || inst.definitionId }}
                </button>
              </TableCell>
              <TableCell class="font-mono text-xs">v{{ inst.version ?? 0 }}</TableCell>
              <TableCell class="font-mono text-xs text-muted-foreground">{{ inst.correlationId || "—" }}</TableCell>
              <TableCell>
                <Badge v-if="(inst.incidentCount ?? 0) > 0" variant="destructive">{{ inst.incidentCount }}</Badge>
                <span v-else class="text-muted-foreground">—</span>
              </TableCell>
              <TableCell class="text-xs text-muted-foreground">{{ formatDateTime(inst.createdAt) }}</TableCell>
              <TableCell class="text-xs text-muted-foreground">{{ formatDateTime(inst.finishedAt) }}</TableCell>
              <TableCell>
                <Button v-if="list.cancellingIds.value.has(inst.id!)" variant="outline" size="sm" disabled>
                  取消中…
                </Button>
                <Button v-else-if="canCancel(inst.status)" variant="outline" size="sm" @click="askCancel(inst)">
                  取消
                </Button>
                <span v-else class="text-muted-foreground">—</span>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      <!-- Pagination -->
      <div v-if="list.totalPages.value > 1" class="flex justify-center">
        <Pagination
          :page="list.page.value"
          :total="list.total.value"
          :items-per-page="list.pageSize.value"
          :sibling-count="1"
          @update:page="(p: number) => list.setPage(p)"
        >
          <PaginationContent v-slot="{ items }">
            <PaginationPrevious>
              <ChevronLeft :size="16" />
              <span>上一页</span>
            </PaginationPrevious>
            <template v-for="(item, index) in items" :key="index">
              <PaginationItem
                v-if="item.type === 'page'"
                :value="item.value"
                :is-active="item.value === list.page.value"
              >
                {{ item.value }}
              </PaginationItem>
              <PaginationEllipsis v-else :index="index" />
            </template>
            <PaginationNext>
              <span>下一页</span>
              <ChevronRight :size="16" />
            </PaginationNext>
          </PaginationContent>
        </Pagination>
      </div>
    </template>

    <!-- Cancel confirmation (irreversible) -->
    <AlertDialog v-model:open="cancelOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>取消工作流实例？</AlertDialogTitle>
          <AlertDialogDescription>
            即将取消实例「{{ cancelTarget?.name || cancelTarget?.definitionName || cancelTarget?.id }}」。取消后无法恢复运行，请确认。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>返回</AlertDialogCancel>
          <AlertDialogAction @click="confirmCancel">确认取消</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
