<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsByIdPermissionsPage } from './useAdminCompetitionsByIdPermissionsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdPermissionsPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdPermissionsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
