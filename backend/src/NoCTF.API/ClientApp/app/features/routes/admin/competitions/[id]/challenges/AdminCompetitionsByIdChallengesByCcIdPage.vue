<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsTeamsTeamResponse } from '~/api'

import { useAdminCompetitionsByIdChallengesByCcIdPage } from './useAdminCompetitionsByIdChallengesByCcIdPage'
import View from '~/components/views/page/admin/competitions/[id]/challenges/AdminCompetitionsByIdChallengesByCcIdPageView.vue'

const state = bindViewState(useAdminCompetitionsByIdChallengesByCcIdPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
