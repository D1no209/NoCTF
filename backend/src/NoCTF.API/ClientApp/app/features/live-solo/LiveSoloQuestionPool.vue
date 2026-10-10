<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloQuestionPool } from './useLiveSoloQuestionPool'
import LiveSoloQuestionPoolView from '~/components/views/live-solo/LiveSoloQuestionPoolView.vue'
const state = bindViewState(useLiveSoloQuestionPool())
</script>
<template><LiveSoloQuestionPoolView :state="state" /></template>
