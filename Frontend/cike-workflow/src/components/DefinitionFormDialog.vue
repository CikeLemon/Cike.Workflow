<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>{{ definition?.id ? '编辑定义' : '新建定义' }}</DialogTitle>
        <DialogDescription>
          {{ definition?.id ? '修改工作流定义的基本信息' : '在当前目录下创建新的工作流定义' }}
        </DialogDescription>
      </DialogHeader>

      <form @submit="onSubmit" class="space-y-4 min-w-0">
        <FormField v-slot="{ componentField }" name="definitionId">
          <FormItem>
            <FormLabel>定义 ID</FormLabel>
            <FormControl>
              <Input
                :disabled="!!definition?.id"
                placeholder="留空则由系统自动生成"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
            <p v-if="!definition?.id" class="text-xs text-muted-foreground">
              可选。自定义工作流定义的唯一标识，留空则由后端自动生成。
            </p>
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="name">
          <FormItem>
            <FormLabel>名称</FormLabel>
            <FormControl>
              <Input placeholder="如 月度报销审批" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="description">
          <FormItem>
            <FormLabel>描述</FormLabel>
            <FormControl>
              <Textarea
                placeholder="简要描述该定义的用途"
                rows="3"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <div class="grid grid-cols-2 gap-4">
          <FormField v-slot="{ value, handleChange }" name="type">
            <FormItem>
              <FormLabel>类型</FormLabel>
              <Select :model-value="String(value)" @update:model-value="(v) => handleChange(Number(v))">
                <FormControl>
                  <SelectTrigger class="w-full">
                    <SelectValue placeholder="选择类型" />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectItem v-for="opt in typeOptions" :key="opt.value" :value="String(opt.value)">
                    {{ opt.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField v-slot="{ value, handleChange }" name="usableAsActivity">
            <FormItem>
              <FormLabel>可作为活动使用</FormLabel>
              <div class="flex h-9 items-center gap-2">
                <FormControl>
                  <Switch :model-value="value" @update:model-value="handleChange" />
                </FormControl>
                <span class="text-sm text-muted-foreground">
                  {{ value ? '是' : '否' }}
                </span>
              </div>
              <FormMessage />
            </FormItem>
          </FormField>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" @click="close">
            取消
          </Button>
          <Button type="submit" :disabled="isSubmitting">
            {{ isSubmitting ? '提交中...' : (definition?.id ? '保存' : '创建') }}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { watch } from "vue"
import { useForm } from "vee-validate"
import { toTypedSchema } from "@vee-validate/zod"
import * as z from "zod"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import {
  postApiV1WorkflowDefinitions,
  putApiV1WorkflowDefinitionsById,
} from "@/api"
import type { WorkflowDefinitionFolderItemDto, WorkflowDefinitionItemDto, WorkflowDefinitionType } from "@/api"
import { extractApiErrorMessage } from "@/lib/apiError"

const props = defineProps<{
  open: boolean
  definition?: WorkflowDefinitionFolderItemDto | null
  folderId: string
  workspaceId: string
}>()

const emit = defineEmits<{
  (e: "update:open", value: boolean): void
  (e: "saved"): void
}>()

const isEdit = () => !!props.definition?.id

// 类型映射：1=Workflow, 2=AgentWorkflow, 3=Approval
const typeOptions = [
  { value: 1, label: "Workflow" },
  { value: 2, label: "AgentWorkflow" },
  { value: 3, label: "Approval" },
] as const

const formSchema = toTypedSchema(
  z.object({
    definitionId: z.string().max(200).default(""),
    name: z.string().min(1, "名称不能为空").max(200),
    description: z.string().max(2000).default(""),
    type: z.number().int().min(1).max(3),
    usableAsActivity: z.boolean().default(false),
  }),
)

const { handleSubmit, resetForm, isSubmitting, setFieldError } = useForm({
  validationSchema: formSchema,
})

watch(
  () => props.open,
  (val) => {
    if (val) {
      if (props.definition?.data) {
        const data = props.definition.data as WorkflowDefinitionItemDto
        resetForm({
          values: {
            definitionId: data.definitionId ?? "",
            name: data.name ?? "",
            description: data.description ?? "",
            type: data.type ?? 1,
            usableAsActivity: data.usableAsActivity ?? false,
          },
        })
      } else {
        resetForm({
          values: { definitionId: "", name: "", description: "", type: 1, usableAsActivity: false },
        })
      }
    }
  },
)

function close() {
  emit("update:open", false)
}

const onSubmit = handleSubmit(async (values) => {
  const { error } = isEdit()
    ? await putApiV1WorkflowDefinitionsById({
        path: { id: props.definition!.id! },
        body: {
          name: values.name,
          description: values.description,
          type: values.type as WorkflowDefinitionType,
          usableAsActivity: values.usableAsActivity,
        },
      })
    : await postApiV1WorkflowDefinitions({
        body: {
          workspaceId: props.workspaceId,
          folderId: props.folderId,
          definitionId: values.definitionId.trim() || null,
          name: values.name,
          description: values.description,
          type: values.type as WorkflowDefinitionType,
          usableAsActivity: values.usableAsActivity,
        },
      })
  if (error) {
    setFieldError("name", extractApiErrorMessage(error, "保存失败，请稍后重试"))
    return
  }
  close()
  emit("saved")
})
</script>
