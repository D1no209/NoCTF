<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdChallengesIndexPage } from './useAdminCompetitionsByIdChallengesIndexPage'
import View from '~/components/views/page/admin/competitions/[id]/challenges/AdminCompetitionsByIdChallengesIndexPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdChallengesIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
