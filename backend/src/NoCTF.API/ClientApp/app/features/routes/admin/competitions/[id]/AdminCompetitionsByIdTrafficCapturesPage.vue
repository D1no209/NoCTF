<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdTrafficCapturesPage } from './useAdminCompetitionsByIdTrafficCapturesPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdTrafficCapturesPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdTrafficCapturesPage())
</script>

<template>
  <View :state="state" />
</template>
