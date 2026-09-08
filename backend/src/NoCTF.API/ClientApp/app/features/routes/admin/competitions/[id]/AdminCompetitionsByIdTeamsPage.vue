<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdTeamsPage } from './useAdminCompetitionsByIdTeamsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdTeamsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdTeamsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
