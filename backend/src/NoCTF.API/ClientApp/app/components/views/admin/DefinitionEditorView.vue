<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionEditorViewState } from '~/features/admin/useDefinitionEditor'

const viewProps = defineProps<{ state: DefinitionEditorViewState }>()
const { model, parseFailed, DefinitionCheckerSection, DefinitionFlagInjectionSection, DefinitionPatchSection, DefinitionRuntimeSection, mode, disabled } = toRefs(viewProps.state)
</script>

<template>
  <Alert v-if="parseFailed" variant="destructive">
    <AlertDescription> {{ $t('ui.theExistingDefinitionJsonCannotBeParsedAndMayBe') }} </AlertDescription>
  </Alert>

  <FieldGroup v-else-if="model">
    <component :is="DefinitionRuntimeSection" :model="model" :mode="mode" :disabled="disabled" />

    <template v-if="mode === 'Awd'">
      <component :is="DefinitionFlagInjectionSection" :model="model" :disabled="disabled" />
      <component :is="DefinitionCheckerSection" :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <template v-if="mode === 'Awdp'">
      <component :is="DefinitionPatchSection" :model="model" :disabled="disabled" />
      <component :is="DefinitionCheckerSection" :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <FieldDescription> {{ $t('ui.definitionModificationsWillTakeEffectOnInstancesThatAreStarted') }} </FieldDescription>
  </FieldGroup>
</template>
