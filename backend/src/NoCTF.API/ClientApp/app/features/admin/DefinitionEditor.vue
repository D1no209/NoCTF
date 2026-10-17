<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { GameModeValue } from '~/utils/game-config'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '~/api'
import { useDefinitionEditor } from './useDefinitionEditor'
import View from '~/components/views/admin/DefinitionEditorView.vue'

const props = withDefaults(defineProps<{
  modelValue: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract] }>()
const FeatureView = defineFeatureView(View, () => useDefinitionEditor(props, emit))

</script>

<template>
  <FeatureView />
</template>
