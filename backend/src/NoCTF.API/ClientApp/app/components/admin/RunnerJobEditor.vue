<script setup lang="ts">
import type { RunnerJobModel } from '~/utils/game-config'

withDefaults(defineProps<{
  job: RunnerJobModel
  disabled?: boolean
}>(), {
  disabled: false,
})
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>镜像</FieldLabel>
      <Input v-model="job.image" placeholder="registry.example.com/checker:latest" class="font-mono text-sm" :disabled="disabled" />
    </Field>
    <Field>
      <FieldLabel>启动命令</FieldLabel>
      <StringListEditor
        :model-value="job.command"
        placeholder="参数,如 --check"
        add-label="添加参数"
        :disabled="disabled"
        @update:model-value="job.command = $event"
      />
      <FieldDescription>留空使用镜像默认入口;按参数逐项填写,不要做 shell 转义。</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>环境变量</FieldLabel>
      <KeyValueEditor
        :model-value="job.environment"
        key-placeholder="变量名"
        value-placeholder="值"
        add-label="添加环境变量"
        :disabled="disabled"
        @update:model-value="job.environment = $event"
      />
      <FieldDescription>不允许 NOCTF_ 前缀的变量名(平台保留)。</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>超时(秒)</FieldLabel>
      <NullableNumberInput
        :model-value="job.timeoutSeconds"
        :min="1"
        :max="1800"
        placeholder="默认 60"
        :disabled="disabled"
        @update:model-value="job.timeoutSeconds = $event"
      />
    </Field>
  </FieldGroup>
</template>
