<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTheoryAnswerPage } from './useTheoryAnswerPage'
import View from '~/components/views/theory/TheoryAnswerPageView.vue'
const state = bindViewState(useTheoryAnswerPage())
</script>
<template><View :state="state" /></template>
