<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsNewPage } from './useAdminCompetitionsNewPage'
import View from '~/components/views/page/admin/competitions/AdminCompetitionsNewPageView.vue'

const state = bindViewState(useAdminCompetitionsNewPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
