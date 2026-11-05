<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '~/api'
import { useDefinitionEditor } from './useDefinitionEditor'
import View from '~/components/views/admin/DefinitionEditorView.vue'

const props = withDefaults(defineProps<{
  modelValue: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
  disabled?: boolean
  includeRuntime?: boolean
  challengeId?: string
  savedQuestionIds?: string[]
}>(), {
  disabled: false,
  includeRuntime: true,
})
const emit = defineEmits<{ 'update:modelValue': [definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract] }>()
const FeatureView = defineFeatureView(View, () => useDefinitionEditor(props, emit))

</script>

<template>
  <FeatureView />
</template>
