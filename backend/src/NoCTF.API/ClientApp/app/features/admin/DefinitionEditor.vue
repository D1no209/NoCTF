<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '~/api'
import { useDefinitionEditor } from './useDefinitionEditor'
import View from '~/components/views/admin/DefinitionEditorView.vue'
import type { DefinitionModel } from '~/utils/game-config'
import type { CapabilityEditorSection } from './capability-editor'

const props = withDefaults(defineProps<{
  modelValue?: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
  draftModel?: DefinitionModel | null
  section?: CapabilityEditorSection
  disabled?: boolean
  includeRuntime?: boolean
  challengeId?: string
  savedQuestionIds?: string[]
}>(), {
  disabled: false,
  includeRuntime: true,
  section: 'all',
})
const emit = defineEmits<{ 'update:modelValue': [definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract] }>()
const FeatureView = defineFeatureView(View, () => useDefinitionEditor(props, emit))

</script>

<template>
  <FeatureView />
</template>
