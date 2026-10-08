<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ folder?.id ? '重命名目录' : '新建目录' }}</DialogTitle>
        <DialogDescription>
          {{ folder?.id ? '修改目录名称' : '在当前目录下创建新的目录' }}
        </DialogDescription>
      </DialogHeader>

      <form @submit="onSubmit" class="space-y-4">
        <FormField v-slot="{ componentField }" name="name">
          <FormItem>
            <FormLabel>目录名称</FormLabel>
            <FormControl>
              <Input placeholder="如 报销流程" autofocus v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <DialogFooter>
          <Button type="button" variant="outline" @click="close">
            取消
          </Button>
          <Button type="submit" :disabled="isSubmitting">
            {{ isSubmitting ? '提交中...' : (folder?.id ? '保存' : '创建') }}
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
import { postApiV1Folders, putApiV1Folders } from "@/api"
import { extractApiErrorMessage } from "@/lib/apiError"

const props = defineProps<{
  open: boolean
  folder?: { id: string; name: string } | null
  parentId: string
  workspaceId: string
}>()

const emit = defineEmits<{
  (e: "update:open", value: boolean): void
  (e: "saved"): void
}>()

const isEdit = () => !!props.folder?.id

const formSchema = toTypedSchema(
  z.object({
    name: z.string().min(1, "目录名称不能为空").max(200),
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
        values: { name: props.folder?.name ?? "" },
      })
    }
  },
)

function close() {
  emit("update:open", false)
}

const onSubmit = handleSubmit(async (values) => {
  const { error } = isEdit()
    ? await putApiV1Folders({
        query: { folderId: props.folder!.id },
        body: { name: values.name },
      })
    : await postApiV1Folders({
        body: {
          workspaceId: props.workspaceId,
          name: values.name,
          parentId: props.parentId,
        },
      })
  if (error) {
    setFieldError('name', extractApiErrorMessage(error, '保存失败，请稍后重试'))
    return
  }
  close()
  emit("saved")
})
</script>
