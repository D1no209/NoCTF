<script setup lang="ts">
import type { GameModeValue } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  /** definitionJson 字符串(v-model)。 */
  modelValue: string
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [json: string] }>()

const { model, parseFailed } = useDefinitionModel(
  () => props.modelValue,
  () => props.mode,
  json => emit('update:modelValue', json),
)
</script>

<template>
  <Alert v-if="parseFailed" variant="destructive">
    <AlertDescription> {{ $t('现有定义 JSON 无法解析,可能是历史遗留数据。请先在数据库或 API 层面修复后再编辑。') }} </AlertDescription>
  </Alert>

  <FieldGroup v-else-if="model">
    <DefinitionRuntimeSection :model="model" :mode="mode" :disabled="disabled" />

    <template v-if="mode === 'Awd'">
      <DefinitionFlagInjectionSection :model="model" :disabled="disabled" />
      <DefinitionCheckerSection :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <template v-if="mode === 'Awdp'">
      <DefinitionPatchSection :model="model" :disabled="disabled" />
      <DefinitionCheckerSection :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <FieldDescription> {{ $t('定义修改对未来启动/重置的实例生效,已存在的运行实例不受影响。') }} </FieldDescription>
  </FieldGroup>
</template>
