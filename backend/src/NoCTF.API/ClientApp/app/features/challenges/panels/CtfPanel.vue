<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'

import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useCtfPanel } from './useCtfPanel'
import View from '~/components/views/challenges/panels/CtfPanelView.vue'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>()
const emit = defineEmits<{ submitted: []; remainingChanged: [remaining: number | null] }>()
const FeatureView = defineFeatureView(View, () => useCtfPanel(props, emit))

</script>

<template>
  <FeatureView />
</template>
