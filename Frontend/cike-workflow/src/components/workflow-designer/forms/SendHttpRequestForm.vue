<template>
  <div class="space-y-3">
    <div class="space-y-2">
      <div class="text-xs font-medium text-muted-foreground">请求</div>

      <ExpressionEditor :expression="expr('method')" :designer="designer" label="方法" literal-default="GET">
        <template #default="{ value, commit }">
          <Select :model-value="text(value)" @update:model-value="(v) => commit(String(v))">
            <SelectTrigger size="sm" class="w-full text-xs">
              <SelectValue class="block! min-w-0 truncate" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="method in METHODS" :key="method" :value="method">{{ method }}</SelectItem>
            </SelectContent>
          </Select>
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('url')" :designer="designer" label="URL" literal-default="">
        <template #default="{ value, commit }">
          <UiInput
            :model-value="text(value)"
            placeholder="https://"
            @change="(e: Event) => commitText(value, (e.target as HTMLInputElement).value, commit)"
          />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('authorization')" :designer="designer" label="认证" literal-default="">
        <template #default="{ value, commit }">
          <UiInput
            :model-value="text(value)"
            placeholder="Bearer …"
            @change="(e: Event) => commitText(value, (e.target as HTMLInputElement).value, commit)"
          />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('contentType')" :designer="designer" label="Content-Type" literal-default="">
        <template #default="{ value, commit }">
          <UiInput
            :model-value="text(value)"
            placeholder="application/json"
            @change="(e: Event) => commitText(value, (e.target as HTMLInputElement).value, commit)"
          />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('content')" :designer="designer" label="内容（JSON）" :literal-default="{}">
        <template #default="{ value, commit, readonly }">
          <CodeLiteralEditor
            :value="value"
            :readonly="readonly"
            @blur="(raw) => commitText(value, raw, commit)"
          />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('requestHeaders')" :designer="designer" label="请求头" :literal-default="{ headers: {} }">
        <template #default="{ value, commit, readonly }">
          <HeadersKeyValueEditor :value="value" :readonly="readonly" @commit="(next) => commit(next)" />
        </template>
      </ExpressionEditor>
    </div>

    <div class="space-y-2">
      <div class="text-xs font-medium text-muted-foreground">响应与完成</div>

      <ExpressionEditor :expression="expr('timeoutInterval')" :designer="designer" label="超时（秒）" :literal-default="0">
        <template #default="{ value, commit }">
          <UiInput
            type="number"
            :model-value="text(value)"
            @change="(e: Event) => commitText(value, (e.target as HTMLInputElement).value, commit)"
          />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('responseErrorCodes')" :designer="designer" label="错误状态码" :literal-default="[]">
        <template #default="{ value, commit, readonly }">
          <StatusCodeMultiSelect :model-value="codes(value)" :readonly="readonly" @commit="(next) => commit(next)" />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('suspendOnStatusCodes')" :designer="designer" label="挂起状态码" :literal-default="[]">
        <template #default="{ value, commit, readonly }">
          <StatusCodeMultiSelect :model-value="codes(value)" :readonly="readonly" @commit="(next) => commit(next)" />
        </template>
      </ExpressionEditor>

      <ExpressionEditor :expression="expr('waitForCompletion')" :designer="designer" label="等待完成" :literal-default="false">
        <template #default="{ value, commit, readonly }">
          <div class="flex h-8 items-center">
            <Switch :model-value="value === true" :disabled="readonly" @update:model-value="(v: boolean) => commit(v)" />
          </div>
        </template>
      </ExpressionEditor>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { ExpressionLike } from "@/core/designer/expression"
import { coerceLiteralValue } from "@/core/designer/form"
import CodeLiteralEditor from "./CodeLiteralEditor.vue"
import ExpressionEditor from "../ExpressionEditor.vue"
import HeadersKeyValueEditor from "./HeadersKeyValueEditor.vue"
import StatusCodeMultiSelect from "./StatusCodeMultiSelect.vue"

/**
 * Single-column, grouped layout (n8n-style side panel): the ~300px dock leaves
 * ~90px per cell in a two-column grid once each row's 32px type-icon trigger
 * is deducted, which truncated the 45-code default error list beyond reading.
 */
const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

const METHODS = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"]

const activity = computed(() => props.activity as unknown as Record<string, { expression: ExpressionLike }>)

function expr(key: string): ExpressionLike {
  return activity.value[key].expression
}

function text(value: unknown): string {
  return value == null ? "" : typeof value === "object" ? JSON.stringify(value) : String(value)
}

function codes(value: unknown): number[] {
  return Array.isArray(value) ? value.filter((entry): entry is number => typeof entry === "number") : []
}

function commitText(value: unknown, raw: string, commit: (to: unknown) => void): void {
  const to = coerceLiteralValue(value, raw)
  if (to !== undefined) commit(to)
}
</script>
