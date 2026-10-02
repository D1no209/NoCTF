<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdAnnouncementsPage } from './useAdminCompetitionsByIdAnnouncementsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdAnnouncementsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdAnnouncementsPage())
</script>

<template>
  <View :state="state" />
</template>
