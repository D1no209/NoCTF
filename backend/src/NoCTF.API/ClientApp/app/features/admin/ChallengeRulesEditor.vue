<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { GameModeValue } from '~/utils/game-config'
import type { NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract, NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '~/api/models'

import { useChallengeRulesEditor } from './useChallengeRulesEditor'
import View from '~/components/views/admin/ChallengeRulesEditorView.vue'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  rules?: NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract | null
  inheritedConfiguration?: NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
  hiddenKeys?: string[]
}>(), {
  rules: null,
  inheritedConfiguration: null,
  readonly: false,
  loading: false,
  saving: false,
  hiddenKeys: () => [],
})
const emit = defineEmits<{ save: [rules: NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract] }>()
const state = bindViewState(useChallengeRulesEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
