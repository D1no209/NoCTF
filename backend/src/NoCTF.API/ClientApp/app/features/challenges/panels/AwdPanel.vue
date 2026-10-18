<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'

import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useAwdPanel } from './useAwdPanel'
import View from '~/components/views/challenges/panels/AwdPanelView.vue'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>()
const emit = defineEmits<{ submitted: []; remainingChanged: [remaining: number | null] }>()
const FeatureView = defineFeatureView(View, () => useAwdPanel(props, emit))

</script>

<template>
  <FeatureView />
</template>
