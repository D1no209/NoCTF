<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdSubmissionsPage } from './useAdminCompetitionsByIdSubmissionsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdSubmissionsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdSubmissionsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
