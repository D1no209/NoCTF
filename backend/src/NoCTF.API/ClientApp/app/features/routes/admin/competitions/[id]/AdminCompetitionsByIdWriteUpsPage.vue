<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionsByIdWriteUpsPage } from '~/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdWriteUpsPageView.vue'

const state = bindViewState(useCompetitionsByIdWriteUpsPage({ management: true }))
</script>

<template>
  <View :state="state" />
</template>
