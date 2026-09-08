<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminCompetitionsIndexPage } from './useAdminCompetitionsIndexPage'
import View from '~/components/views/page/admin/competitions/AdminCompetitionsIndexPageView.vue'

const state = bindViewState(useAdminCompetitionsIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
