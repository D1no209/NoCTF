<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
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
const state = bindViewState(useDefinitionEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
