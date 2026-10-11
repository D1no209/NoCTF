<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloReview } from './useLiveSoloReview'
import LiveSoloReviewView from '~/components/views/live-solo/LiveSoloReviewView.vue'
const props = defineProps<{ competitionId: string; matchId: string; staff: boolean; roundId?: string }>()
const state = bindViewState(useLiveSoloReview(props))
</script>
<template><LiveSoloReviewView :state="state" /></template>
