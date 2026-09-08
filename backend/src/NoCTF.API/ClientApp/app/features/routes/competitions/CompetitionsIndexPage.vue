<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useCompetitionsIndexPage } from './useCompetitionsIndexPage'
import View from '~/components/views/page/competitions/CompetitionsIndexPageView.vue'

const state = bindViewState(useCompetitionsIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
