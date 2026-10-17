<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'

import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useKohPanel } from './useKohPanel'
import View from '~/components/views/challenges/panels/KohPanelView.vue'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()
const FeatureView = defineFeatureView(View, () => useKohPanel(props))

</script>

<template>
  <FeatureView />
</template>
