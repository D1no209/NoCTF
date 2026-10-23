<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdScorePresetsPage } from './useAdminCompetitionsByIdScorePresetsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdScorePresetsPageView.vue'
const state = bindViewState(useAdminCompetitionsByIdScorePresetsPage())
</script>
<template><View :state="state" /></template>
