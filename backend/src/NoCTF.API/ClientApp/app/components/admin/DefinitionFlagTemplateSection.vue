<script setup lang="ts">
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'
import { emptyFlagTemplate, FlagSource } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

function toggleFlagTemplate(enabled: boolean): void {
  props.model.flagTemplate = enabled ? emptyFlagTemplate() : null
}
</script>

<template>
  <FieldSet v-if="mode === 'Awd'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('Flag 模板') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-flag-template"
        :model-value="model.flagTemplate !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagTemplate($event === true)"
      />
      <FieldLabel for="def-has-flag-template" class="font-normal">{{ $t('自定义每队 Flag 生成模板') }}</FieldLabel>
    </Field>
    <FlagTemplateEditor v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
    <FieldDescription v-else>{{ $t('未配置时使用竞赛级 Flag 模板或平台默认。') }}</FieldDescription>
  </FieldSet>

  <FieldSet
    v-else-if="mode === 'Ctf' && model.runtime?.flagSource === FlagSource.PerTeam"
    class="rounded-md border p-4"
  >
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('动态 Flag 模板覆盖') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-ctf-flag-template"
        :model-value="model.flagTemplate !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagTemplate($event === true)"
      />
      <FieldLabel for="def-has-ctf-flag-template" class="font-normal"> {{ $t('为本题覆盖竞赛级动态 Flag 模板') }} </FieldLabel>
    </Field>
    <FlagTemplateEditor v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
    <FieldDescription v-else> {{ $t('未配置时使用竞赛级模板；只影响今后生成的每队容器 Flag。') }} </FieldDescription>
  </FieldSet>
</template>
