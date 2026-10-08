<script setup lang="ts">
import SingleWriteUpSettingsView from '~/components/views/writeups/SingleWriteUpSettingsView.vue'
import { useSingleWriteUpSettings } from './useSingleWriteUpSettings'
import { bindViewState } from '~/features/shared/view-state'
const props = defineProps<{ competitionId: string; competitionChallengeId?: string; canWrite: boolean }>()
const emit = defineEmits<{ saved: [] }>()
const state = bindViewState(useSingleWriteUpSettings(props, () => emit('saved')))
</script>
<template><SingleWriteUpSettingsView :state="state" /></template>
