<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionEditorViewState } from '~/features/admin/useDefinitionEditor'

const viewProps = defineProps<{ state: DefinitionEditorViewState }>()
const { model, parseFailed, DefinitionCheckerSection, DefinitionFlagInjectionSection, DefinitionPatchSection, DefinitionRuntimeSection, CtfInteraction, showInteractionKind, setInteractionKind, mode, disabled } = toRefs(viewProps.state)
</script>

<template>
  <Alert v-if="parseFailed" variant="destructive">
    <AlertDescription> {{ $t('ui.theExistingDefinitionJsonCannotBeParsedAndMayBe') }} </AlertDescription>
  </Alert>

  <FieldGroup v-else-if="model">
    <Field v-if="showInteractionKind">
      <FieldLabel for="ctf-interaction-kind">{{ $t('ui.completionMethod') }}</FieldLabel>
      <Select
        :model-value="model.interactionKind === CtfInteraction.PatchVerification ? 'PatchVerification' : 'FlagSubmission'"
        :disabled="disabled"
        @update:model-value="setInteractionKind"
      >
        <SelectTrigger id="ctf-interaction-kind" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="FlagSubmission">{{ $t('ui.flagSubmission') }}</SelectItem>
            <SelectItem value="PatchVerification">{{ $t('ui.patchVerification') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <FieldDescription>{{ $t('ui.ctfCompletionMethodDescription') }}</FieldDescription>
    </Field>

    <component :is="DefinitionRuntimeSection" :model="model" :mode="mode" :disabled="disabled" />

    <template v-if="mode === 'Awd'">
      <component :is="DefinitionFlagInjectionSection" :model="model" :disabled="disabled" />
      <component :is="DefinitionCheckerSection" :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <template v-if="mode === 'Awdp' || (mode === 'Ctf' && model.interactionKind === CtfInteraction.PatchVerification)">
      <component :is="DefinitionPatchSection" :model="model" :disabled="disabled" />
      <component :is="DefinitionCheckerSection" :model="model" :mode="mode" :disabled="disabled" />
    </template>

    <FieldDescription> {{ $t('ui.definitionModificationsWillTakeEffectOnInstancesThatAreStarted') }} </FieldDescription>
  </FieldGroup>
</template>
