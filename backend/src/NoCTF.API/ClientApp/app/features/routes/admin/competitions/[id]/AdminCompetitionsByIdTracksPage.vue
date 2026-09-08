<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest } from '~/api'

import { useAdminCompetitionsByIdTracksPage } from './useAdminCompetitionsByIdTracksPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdTracksPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdTracksPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
