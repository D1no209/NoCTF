<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloQuestions } from './useLiveSoloQuestions'
import type { NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse } from '~/api'
import LiveSoloQuestionsView from '~/components/views/live-solo/LiveSoloQuestionsView.vue'
const props = defineProps<{ competitionId: string; matchId: string; round: NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse; canSubmit: boolean; dockTarget: string; initialQuestions?: import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloQuestionResponse[] }>()
const state = bindViewState(useLiveSoloQuestions(props))
</script>
<template><LiveSoloQuestionsView :state="state" /></template>
