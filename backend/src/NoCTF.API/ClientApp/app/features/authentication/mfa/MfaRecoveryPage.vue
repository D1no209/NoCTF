<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useMfaRecoveryPage } from './useMfaRecoveryPage'
import View from '~/components/views/authentication/MfaRecoveryPageView.vue'
const state = bindViewState(useMfaRecoveryPage())
</script>
<template><View :state="state" /></template>
