<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdWebhooksPage } from './useAdminCompetitionsByIdWebhooksPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdWebhooksPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdWebhooksPage())
</script>

<template>
  <View :state="state" />
</template>
