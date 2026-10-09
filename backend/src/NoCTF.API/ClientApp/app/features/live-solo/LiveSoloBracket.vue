<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloBracket } from './useLiveSoloBracket'
import LiveSoloBracketView from '~/components/views/live-solo/LiveSoloBracketView.vue'
const state = bindViewState(useLiveSoloBracket())
</script>
<template><LiveSoloBracketView :state="state" /></template>
