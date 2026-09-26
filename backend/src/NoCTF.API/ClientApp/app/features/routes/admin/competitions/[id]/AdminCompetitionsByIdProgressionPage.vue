<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminCompetitionsByIdProgressionPage } from './useAdminCompetitionsByIdProgressionPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdProgressionPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdProgressionPage())
</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
