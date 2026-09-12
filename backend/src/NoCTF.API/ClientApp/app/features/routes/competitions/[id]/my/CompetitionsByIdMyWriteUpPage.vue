<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionsByIdMyWriteUpPage } from './useCompetitionsByIdMyWriteUpPage'
import View from '~/components/views/page/competitions/[id]/my/CompetitionsByIdMyWriteUpPageView.vue'

const state = bindViewState(useCompetitionsByIdMyWriteUpPage())
</script>

<template><View :state="state" /></template>
