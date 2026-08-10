<script setup lang="ts">
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'
import { emptyRuntimeTemplate } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

function toggleRuntime(enabled: boolean): void {
  props.model.runtime = enabled ? emptyRuntimeTemplate(props.mode) : null
}
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('运行环境') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-runtime"
        :model-value="model.runtime !== null"
        :disabled="disabled"
        @update:model-value="toggleRuntime($event === true)"
      />
      <FieldLabel for="def-has-runtime" class="font-normal">{{ $t('选手需要在线运行环境(容器靶机)') }}</FieldLabel>
    </Field>
    <DefinitionRuntime
      v-if="model.runtime"
      :runtime="model.runtime"
      :mode="mode"
      :disabled="disabled"
    />
    <FieldDescription v-else> {{ $t('纯静态题(如下载附件分析)不需要运行环境。') }} </FieldDescription>
  </FieldSet>
</template>
