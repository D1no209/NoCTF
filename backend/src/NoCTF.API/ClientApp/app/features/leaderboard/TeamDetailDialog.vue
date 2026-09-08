<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { ChallengeInfo, TrendSeries } from '~/features/leaderboard/types'
import { useTeamDetailDialog } from './useTeamDetailDialog'
import View from '~/components/views/leaderboard/TeamDetailDialogView.vue'

const props = defineProps<{
  competitionId: string
  mode?: string | null
  entry: {
    teamId?: string
    teamName?: string
    rank?: number
    score?: number
    solveCount?: number
    attackScore?: number
    defenseScore?: number
    penaltyScore?: number
    cells?: Array<{ competitionChallengeId?: string; attackScore?: number; defenseScore?: number }>
  } | null
  series?: TrendSeries | null
  challenges: ChallengeInfo[]
  rangeStart?: string | null
  rangeEnd?: string | null
}>()
const open = defineModel<boolean>('open', { default: false })
const state = bindViewState(useTeamDetailDialog(props, open))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
