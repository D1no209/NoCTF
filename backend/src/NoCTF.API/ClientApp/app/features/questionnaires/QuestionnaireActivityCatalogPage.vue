<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useQuestionnaireActivityCatalogPage } from './useQuestionnaireActivityCatalogPage'
import View from '~/components/views/questionnaires/QuestionnaireActivityCatalogPageView.vue'
const state = bindViewState(useQuestionnaireActivityCatalogPage())
</script>
<template><View :state="state" /></template>
