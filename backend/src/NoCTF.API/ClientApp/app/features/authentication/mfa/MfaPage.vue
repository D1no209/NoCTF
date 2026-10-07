<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useMfaPage } from './useMfaPage'
import View from '~/components/views/authentication/MfaPageView.vue'
const state = bindViewState(useMfaPage())
</script>
<template><View :state="state" /></template>
