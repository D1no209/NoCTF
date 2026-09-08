<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol } from '~/api'

import { useAdminCompetitionsByIdCheatsPage } from './useAdminCompetitionsByIdCheatsPage'
import View from '~/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdCheatsPageView.vue'

type CheatIncidentStatus = NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol

const state = bindViewState(useAdminCompetitionsByIdCheatsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
