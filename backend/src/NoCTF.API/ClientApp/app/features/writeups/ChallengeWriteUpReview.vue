<script setup lang="ts">
import View from '~/components/views/writeups/ChallengeWriteUpReviewView.vue'
import { bindViewState } from '~/features/shared/view-state'
import { useChallengeWriteUpReview } from './useChallengeWriteUpReview'
const props = defineProps<{ competitionId: string }>()
const state = bindViewState(useChallengeWriteUpReview(props))
</script>
<template><View :state="state" /></template>
