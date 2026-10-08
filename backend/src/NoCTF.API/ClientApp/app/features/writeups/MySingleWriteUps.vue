<script setup lang="ts">
import View from '~/components/views/writeups/MySingleWriteUpsView.vue'
import { bindViewState } from '~/features/shared/view-state'
import { useMySingleWriteUps } from './useMySingleWriteUps'
const props = defineProps<{ competitionId: string }>()
const state = bindViewState(useMySingleWriteUps(props))
</script>
<template><View :state="state" /></template>
