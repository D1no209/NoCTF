<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTheoryQuestionEditorPage } from './useTheoryQuestionEditorPage'
import View from '~/components/views/theory/TheoryQuestionEditorPageView.vue'
const state = bindViewState(useTheoryQuestionEditorPage())
</script>
<template><View :state="state" /></template>
