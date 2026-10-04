<script setup lang="ts">import type { UiMessage } from '../../utils/i18n'

import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol, NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse } from '~/api/models'

import type { ScoreboardChallengeColumnGroup } from '~/utils/scoreboard'
import type { TrendSeries } from '~/features/leaderboard/types'
import { useScoreboardTeamDetailDialog } from './useScoreboardTeamDetailDialog'
import View from '~/components/views/leaderboard/ScoreboardTeamDetailDialogView.vue'

const props = defineProps<{
  mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null
  team: NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse | null
  teams: NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse[]
  columnGroups: ScoreboardChallengeColumnGroup[]
  trendSeries?: TrendSeries[]
  trendLoading?: boolean
  trendError?: UiMessage | null
  trendRangeStart?: string | null
  trendRangeEnd?: string | null
  trendRevision?: string | number | null
}>()
const emit = defineEmits<{ retryTrends: [] }>()
const open = defineModel<boolean>('open', { default: false })
const state = bindViewState(useScoreboardTeamDetailDialog(props, emit, open))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
