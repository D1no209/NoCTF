<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionFlagTemplateSectionViewState } from '~/features/admin/useDefinitionFlagTemplateSection'

const viewProps = defineProps<{ state: DefinitionFlagTemplateSectionViewState }>()
const { FlagSource, toggleFlagTemplate, FlagTemplateEditor, model, mode, disabled } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet v-if="mode === 'Awd'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('administration.label.flagTemplate') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-flag-template"
        :model-value="model.flagTemplate !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagTemplate($event === true)"
      />
      <FieldLabel for="def-has-flag-template" class="font-normal">{{ $t('administration.definitionFlag.description.customizeFlagGenerationTemplate') }}</FieldLabel>
    </Field>
    <component :is="FlagTemplateEditor" v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
    <FieldDescription v-else>{{ $t('administration.definitionFlag.description.configuredCompetitionGradeFlag') }}</FieldDescription>
  </FieldSet>

  <FieldSet
    v-else-if="mode === 'Ctf' && model.runtime?.flagSource === FlagSource.PerTeam"
    class="rounded-md border p-4"
  >
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('administration.label.dynamicFlagTemplateOverride') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-ctf-flag-template"
        :model-value="model.flagTemplate !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagTemplate($event === true)"
      />
      <FieldLabel for="def-has-ctf-flag-template" class="font-normal"> {{ $t('administration.definitionFlag.description.coverCompetitionLevelDynamic') }} </FieldLabel>
    </Field>
    <component :is="FlagTemplateEditor" v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
    <FieldDescription v-else> {{ $t('administration.definitionFlag.description.competitionLevelTemplateConfigured') }} </FieldDescription>
  </FieldSet>
</template>
