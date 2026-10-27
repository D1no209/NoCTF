<script setup lang="ts">
import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse } from '~/api'
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloTournamentMap } from './useLiveSoloTournamentMap'
import LiveSoloTournamentMapView from '~/components/views/live-solo/LiveSoloTournamentMapView.vue'
const props = defineProps<{ bracket: NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse; connected?: boolean; syncing?: boolean }>()
const emit = defineEmits<{ enter: [id: string] }>()
const state = bindViewState(useLiveSoloTournamentMap(props, emit))
</script>
<template><LiveSoloTournamentMapView :state="state" /></template>
