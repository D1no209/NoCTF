<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useMfaPlatformSettings } from './useMfaPlatformSettings'
import View from '~/components/views/authentication/MfaPlatformSettingsView.vue'
const state = bindViewState(useMfaPlatformSettings())
</script>
<template><View :state="state" /></template>
