<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useQuestionnaireAnswerPage } from './useQuestionnaireAnswerPage'
import View from '~/components/views/questionnaires/QuestionnaireAnswerPageView.vue'
const state = bindViewState(useQuestionnaireAnswerPage())
</script>
<template><View :state="state" /></template>
