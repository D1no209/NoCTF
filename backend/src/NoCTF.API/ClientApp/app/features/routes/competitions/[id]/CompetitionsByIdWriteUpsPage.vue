<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionsByIdWriteUpsPage } from './useCompetitionsByIdWriteUpsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdWriteUpsPageView.vue'

const state = bindViewState(useCompetitionsByIdWriteUpsPage())
</script>

<template><View :state="state" /></template>
