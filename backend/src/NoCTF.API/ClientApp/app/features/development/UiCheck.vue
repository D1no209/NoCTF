<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useUiCheck } from './useUiCheck'
import View from '~/components/views/development/UiCheckView.vue'
const state = bindViewState(useUiCheck())
</script>

<template><View :state="state" /></template>
