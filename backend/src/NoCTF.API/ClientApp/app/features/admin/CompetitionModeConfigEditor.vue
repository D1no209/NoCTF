<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { GameModeValue } from '~/utils/game-config'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '~/api'

import { useCompetitionModeConfigEditor } from './useCompetitionModeConfigEditor'
import View from '~/components/views/admin/CompetitionModeConfigEditorView.vue'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  configuration?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}>(), {
  configuration: null,
  readonly: false,
  loading: false,
  saving: false,
})
const emit = defineEmits<{ save: [configuration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract] }>()
const state = bindViewState(useCompetitionModeConfigEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
