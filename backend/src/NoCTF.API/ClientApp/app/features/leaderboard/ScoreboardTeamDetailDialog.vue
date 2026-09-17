<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsCompetitionsScoreboardTeamResponse } from '~/api'

import type { ScoreboardChallengeColumnGroup } from '~/utils/scoreboard'
import type { TrendSeries } from '~/features/leaderboard/types'
import { useScoreboardTeamDetailDialog } from './useScoreboardTeamDetailDialog'
import View from '~/components/views/leaderboard/ScoreboardTeamDetailDialogView.vue'

const props = defineProps<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null
  teams: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[]
  columnGroups: ScoreboardChallengeColumnGroup[]
  trendSeries?: TrendSeries[]
  trendLoading?: boolean
  trendError?: string | null
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
