<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionEditorViewState } from '~/features/admin/useDefinitionEditor'
const props = defineProps<{ state: DefinitionEditorViewState }>()
const { model, parseFailed, disabled, includeRuntime, challengeId, savedQuestionIds, options, setCapability, DefinitionRuntimeSection, DefinitionFlagTemplateSection,
  DefinitionFlagInjectionSection, DefinitionPatchSection, DefinitionCheckerSection, QuestionnaireCapabilityEditor, ControlCapabilityEditor } = toRefs(props.state)
</script>

<template>
  <Alert v-if="parseFailed" variant="destructive"><AlertDescription>{{ $t('administration.definitionEditor.validation.existingDefinitionFormat') }}</AlertDescription></Alert>
  <FieldGroup v-else-if="model">
    <FieldSet>
      <FieldLegend>{{ $t('administration.capability.title') }}</FieldLegend>
      <Field v-for="option in options" :key="option.kind" orientation="horizontal">
        <Checkbox :id="`capability-${option.kind}`" :model-value="model.capabilities.includes(option.kind)" :disabled="disabled"
          @update:model-value="setCapability(option.kind, $event === true)" />
        <FieldLabel :for="`capability-${option.kind}`">{{ $t(option.label) }}</FieldLabel>
      </Field>
    </FieldSet>
    <Separator />
    <component v-if="includeRuntime" :is="DefinitionRuntimeSection" :model="model" :disabled="disabled" />
    <template v-if="model.capabilities.includes('Break')">
      <component :is="DefinitionFlagTemplateSection" :model="model" :disabled="disabled" />
      <component v-if="model.runtime" :is="DefinitionFlagInjectionSection" :model="model" :disabled="disabled" />
    </template>
    <component v-if="model.capabilities.includes('Fix')" :is="DefinitionPatchSection" :model="model" :disabled="disabled" />
    <component v-if="model.capabilities.includes('Check')" :is="DefinitionCheckerSection" :model="model" :disabled="disabled" />
    <component v-if="model.capabilities.includes('Control')" :is="ControlCapabilityEditor" :model="model" :disabled="disabled" />
    <component v-if="model.capabilities.includes('Questionnaire')" :is="QuestionnaireCapabilityEditor" :model="model" :disabled="disabled" :challenge-id="challengeId" :saved-question-ids="savedQuestionIds" />
  </FieldGroup>
</template>
