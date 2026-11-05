<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionFlagTemplateSectionViewState } from '~/features/admin/useDefinitionFlagTemplateSection'

const viewProps = defineProps<{ state: DefinitionFlagTemplateSectionViewState }>()
const { toggleFlagTemplate, FlagTemplateEditor, model, disabled, services, environmentVariable, setEnvironmentVariable } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet>
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
    <Field v-for="service in services" :key="service.name">
      <FieldLabel>{{ $t('administration.label.flagEnvironmentVariableName') }} · {{ service.name }}</FieldLabel>
      <Input :model-value="environmentVariable(service.name)" :disabled="disabled" @update:model-value="setEnvironmentVariable(service.name, $event)" />
    </Field>
  </FieldSet>

</template>
