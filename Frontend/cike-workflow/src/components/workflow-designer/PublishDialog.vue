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
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"

const props = defineProps<{ designer: WorkflowDesignerState; open: boolean }>()

const emit = defineEmits<{ "update:open": [open: boolean] }>()

const note = ref("")

watch(
  () => props.open,
  (open) => {
    if (open) note.value = ""
  },
)

async function confirm() {
  const ok = await props.designer.publish(note.value.trim() || undefined)
  if (ok) emit("update:open", false)
}
</script>

<template>
  <Dialog :open="open" @update:open="(value: boolean) => emit('update:open', value)">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>发布工作流</DialogTitle>
        <DialogDescription>
          将当前画布内容作为一个已发布版本提交。当前版本 v{{ designer.version.value }}。
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-2">
        <Label for="publish-note">发布备注（可选）</Label>
        <Textarea
          id="publish-note"
          v-model="note"
          placeholder="简要描述本次发布的变更"
          rows="3"
        />
      </div>

      <!-- Same failure text as the canvas banner: cap + scroll so a backend
           stack trace cannot stretch the dialog off-screen. -->
      <p v-if="designer.saveError.value" class="max-h-32 overflow-y-auto text-xs whitespace-pre-wrap break-words text-destructive">
        {{ designer.saveError.value }}
      </p>

      <DialogFooter>
        <Button variant="outline" @click="emit('update:open', false)">取消</Button>
        <Button :disabled="designer.saving.value" @click="confirm">
          {{ designer.saving.value ? "发布中…" : "发布" }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
