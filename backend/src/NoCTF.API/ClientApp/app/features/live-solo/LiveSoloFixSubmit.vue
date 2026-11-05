<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsLiveSoloLiveSoloQuestionResponse as Question } from '~/api'
import { useLiveSoloFixSubmit } from './useLiveSoloFixSubmit'
import LiveSoloFixSubmitView from '~/components/views/live-solo/LiveSoloFixSubmitView.vue'
const props = defineProps<{ competitionId: string; matchId: string; roundId: string; question: Question; canSubmit: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const state = bindViewState(useLiveSoloFixSubmit(props, emit))
</script>
<template><LiveSoloFixSubmitView :state="state" /></template>
