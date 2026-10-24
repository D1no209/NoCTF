<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdDeliveriesPage } from './useAdminCompetitionsByIdDeliveriesPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdDeliveriesPageView.vue'
const state = bindViewState(useAdminCompetitionsByIdDeliveriesPage())
</script>
<template><View :state="state" /></template>
