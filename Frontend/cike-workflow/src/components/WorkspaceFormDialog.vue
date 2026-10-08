<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ workspace?.id ? '编辑空间' : '新建空间' }}</DialogTitle>
        <DialogDescription>
          {{ workspace?.id ? '修改工作空间的名称和描述' : '创建一个新的工作空间' }}
        </DialogDescription>
      </DialogHeader>

      <form @submit="onSubmit" class="space-y-4 min-w-0">
        <FormField v-slot="{ componentField }" name="code">
          <FormItem>
            <FormLabel>编码</FormLabel>
            <FormControl>
              <Input
                placeholder="如 FIN、CRM"
                :disabled="!!workspace?.id"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
            <p v-if="workspace?.id" class="text-xs text-muted-foreground">
              编码创建后不可修改
            </p>
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="name">
          <FormItem>
            <FormLabel>名称</FormLabel>
            <FormControl>
              <Input placeholder="如 财务系统" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="description">
          <FormItem>
            <FormLabel>描述</FormLabel>
            <FormControl>
              <Textarea
                placeholder="简要描述该空间的用途"
                rows="3"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <DialogFooter>
          <Button type="button" variant="outline" @click="close">
            取消
          </Button>
          <Button type="submit" :disabled="isSubmitting">
            {{ isSubmitting ? '提交中...' : (workspace?.id ? '保存' : '创建') }}
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
import { postApiV1Workspaces, putApiV1Workspaces } from "@/api"
import type { WorkspaceItemDto } from "@/api"
import { extractApiErrorMessage } from "@/lib/apiError"

const props = defineProps<{
  open: boolean
  workspace?: WorkspaceItemDto | null
}>()

const emit = defineEmits<{
  (e: "update:open", value: boolean): void
  (e: "saved"): void
}>()

const isEdit = () => !!props.workspace?.id

const formSchema = toTypedSchema(
  z.object({
    code: z.string().max(50),
    name: z.string().min(1, "名称不能为空").max(200),
    description: z.string().max(2000).default(""),
  }),
)

const { handleSubmit, resetForm, isSubmitting, setFieldError } = useForm({
  validationSchema: formSchema,
})

watch(
  () => props.open,
  (val) => {
    if (val) {
      resetForm({
        values: {
          code: props.workspace?.code ?? "",
          name: props.workspace?.name ?? "",
          description: props.workspace?.description ?? "",
        },
      })
    }
  },
)

function close() {
  emit("update:open", false)
}

const onSubmit = handleSubmit(async (values) => {
  const { error } = isEdit()
    ? await putApiV1Workspaces({
        query: { workspaceId: props.workspace!.id! },
        body: { name: values.name, description: values.description },
      })
    : await postApiV1Workspaces({
        body: {
          code: values.code,
          name: values.name,
          description: values.description,
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
