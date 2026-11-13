<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdCredentialsPage } from './useAdminCompetitionsByIdCredentialsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdCredentialsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdCredentialsPage())
</script>

<template>
  <View :state="state" />
</template>
