<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { GameModeValue } from '~/utils/game-config'
import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '~/api/models'

import { useCompetitionModeConfigEditor } from './useCompetitionModeConfigEditor'
import View from '~/components/views/admin/CompetitionModeConfigEditorView.vue'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  configuration?: NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}>(), {
  configuration: null,
  readonly: false,
  loading: false,
  saving: false,
})
const emit = defineEmits<{ save: [configuration: NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract] }>()
const state = bindViewState(useCompetitionModeConfigEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
