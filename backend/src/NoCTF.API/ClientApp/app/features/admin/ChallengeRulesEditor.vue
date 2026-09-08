<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { GameModeValue } from '~/utils/game-config'

import { useChallengeRulesEditor } from './useChallengeRulesEditor'
import View from '~/components/views/admin/ChallengeRulesEditorView.vue'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  /** 服务器端当前规则 JSON。 */
  json?: string | null
  /** 当前竞赛配置 JSON，用于展示继承后的具体值。 */
  inheritedJson?: string | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
  hiddenKeys?: string[]
}>(), {
  json: null,
  inheritedJson: null,
  readonly: false,
  loading: false,
  saving: false,
  hiddenKeys: () => [],
})
const emit = defineEmits<{ save: [json: string] }>()
const state = bindViewState(useChallengeRulesEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
