<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdExportsPage } from './useAdminCompetitionsByIdExportsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdExportsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdExportsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
