<script setup lang="ts">
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'
import { emptyRunnerJob } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

const isCompose = computed(() => props.model.runtime?.definition.kind === 'compose')

function toggleChecker(enabled: boolean): void {
  props.model.checker = enabled ? { job: emptyRunnerJob(), targetServiceName: '' } : null
}

function toggleCheckerJob(enabled: boolean): void {
  props.model.checkerJob = enabled ? emptyRunnerJob() : null
  if (!enabled) props.model.checkerFixInput = false
}
</script>

<template>
  <FieldSet v-if="mode === 'Awd'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('Checker(服务健康检查)') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-checker"
        :model-value="model.checker !== null"
        :disabled="disabled"
        @update:model-value="toggleChecker($event === true)"
      />
      <FieldLabel for="def-has-checker" class="font-normal">{{ $t('启用周期性服务检查') }}</FieldLabel>
    </Field>
    <template v-if="model.checker">
      <RunnerJobEditor :job="model.checker.job" :disabled="disabled" />
      <Field v-if="isCompose">
        <FieldLabel>{{ $t('目标服务名') }}</FieldLabel>
        <Input
          v-model="model.checker.targetServiceName"
          :placeholder="$t('compose 中的 service 名')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('Compose 运行环境必须指定被检查的服务;单容器时留空。') }}</FieldDescription>
      </Field>
    </template>
  </FieldSet>

  <FieldSet v-else-if="mode === 'Awdp'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('Fix 一次性验证 Checker') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-checker-job"
        :model-value="model.checkerJob !== null"
        :disabled="disabled"
        @update:model-value="toggleCheckerJob($event === true)"
      />
      <FieldLabel for="def-has-checker-job" class="font-normal">{{ $t('启用 Fix 一次性验证 Checker') }}</FieldLabel>
    </Field>
    <template v-if="model.checkerJob">
      <RunnerJobEditor :job="model.checkerJob" :disabled="disabled" />
      <Field orientation="horizontal">
        <Switch
          id="def-checker-fix-input"
          v-model="model.checkerFixInput"
          :disabled="disabled"
        />
        <div class="grid gap-1">
          <FieldLabel for="def-checker-fix-input" class="font-normal">{{ $t('向 Checker 提供 Fix 包') }}</FieldLabel>
          <FieldDescription>
            {{ $t('Checker 启动前将在固定目录 /noctf/fix 获得经过平台验证的 Fix 内容。') }}
          </FieldDescription>
        </div>
      </Field>
    </template>
    <FieldDescription v-else>
      {{ $t('启用 Fix 一次性验证 Checker 后，才可向 Checker 提供 Fix 包。') }}
    </FieldDescription>
  </FieldSet>
</template>
