<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdLeaderboardPage } from './useAdminCompetitionsByIdLeaderboardPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdLeaderboardPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdLeaderboardPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
