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
      <FieldLabel>{{ $t('镜像') }}</FieldLabel>
      <Input v-model="job.image" placeholder="registry.example.com/checker:latest" class="font-mono text-sm" :disabled="disabled" />
    </Field>
    <Field>
      <FieldLabel>{{ $t('启动命令') }}</FieldLabel>
      <StringListEditor
        :model-value="job.command"
        :placeholder="$t('参数,如 --check')"
        :add-label="$t('添加参数')"
        :disabled="disabled"
        @update:model-value="job.command = $event"
      />
      <FieldDescription>{{ $t('留空使用镜像默认入口;按参数逐项填写,不要做 shell 转义。') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('环境变量') }}</FieldLabel>
      <KeyValueEditor
        :model-value="job.environment"
        :key-placeholder="$t('变量名')"
        :value-placeholder="$t('值')"
        :add-label="$t('添加环境变量')"
        :disabled="disabled"
        @update:model-value="job.environment = $event"
      />
      <FieldDescription>{{ $t('不允许 NOCTF_ 前缀的变量名(平台保留)。') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('超时(秒)') }}</FieldLabel>
      <NullableNumberInput
        :model-value="job.timeoutSeconds"
        :min="1"
        :max="1800"
        :placeholder="$t('默认 60')"
        :disabled="disabled"
        @update:model-value="job.timeoutSeconds = $event"
      />
    </Field>
  </FieldGroup>
</template>
