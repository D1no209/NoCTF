<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdDirectionsPage } from './useAdminCompetitionsByIdDirectionsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdDirectionsPageView.vue'
const state = bindViewState(useAdminCompetitionsByIdDirectionsPage())
</script>
<template><View :state="state" /></template>
