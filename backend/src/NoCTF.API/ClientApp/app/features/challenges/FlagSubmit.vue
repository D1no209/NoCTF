<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '~/api'
import { useFlagSubmit } from './useFlagSubmit'
import View from '~/components/views/challenges/FlagSubmitView.vue'

type TrackedSubmission = Pick<
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
  'state' | 'result' | 'failureCode'
> & {
  id: string
}

const props = withDefaults(
  defineProps<{
    competitionId: string
    competitionChallengeId: string
    /** AWD 批量提交:多行输入,一次提交多个 flag */
    multiple?: boolean
    title?: string
    description?: string
    practice?: boolean
    readOnlyJudgement?: boolean
    dockTarget?: string
    maximumAttempts?: number | null
    remainingAttempts?: number | null
    initiallySolved?: boolean
  }>(),
  { multiple: false, title: translate("ui.submitFlag"), description: '', practice: false, readOnlyJudgement: false, dockTarget: '', initiallySolved: false },
)
const emit = defineEmits<{
  evaluated: [result: TrackedSubmission['result']]
  submitted: [gameplayFactIds: string[]]
  remainingChanged: [remaining: number | null]
}>()
const state = bindViewState(useFlagSubmit(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
