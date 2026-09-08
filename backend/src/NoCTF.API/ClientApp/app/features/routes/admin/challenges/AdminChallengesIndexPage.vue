<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminChallengesIndexPage } from './useAdminChallengesIndexPage'
import View from '~/components/views/page/admin/challenges/AdminChallengesIndexPageView.vue'

const state = bindViewState(useAdminChallengesIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
