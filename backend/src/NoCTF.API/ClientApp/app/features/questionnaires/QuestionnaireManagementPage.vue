<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useQuestionnaireManagementPage } from './useQuestionnaireManagementPage'
import View from '~/components/views/questionnaires/QuestionnaireManagementPageView.vue'
const state = bindViewState(useQuestionnaireManagementPage())
</script>
<template><View :state="state" /></template>
