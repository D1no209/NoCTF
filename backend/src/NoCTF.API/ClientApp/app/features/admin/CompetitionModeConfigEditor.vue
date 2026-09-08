<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { GameModeValue } from '~/utils/game-config'

import { useCompetitionModeConfigEditor } from './useCompetitionModeConfigEditor'
import View from '~/components/views/admin/CompetitionModeConfigEditorView.vue'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  /** 服务器端当前配置 JSON。 */
  json?: string | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}>(), {
  json: null,
  readonly: false,
  loading: false,
  saving: false,
})
const emit = defineEmits<{ save: [json: string] }>()
const state = bindViewState(useCompetitionModeConfigEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
