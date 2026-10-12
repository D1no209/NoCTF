<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTheoryManagementPage } from './useTheoryManagementPage'
import View from '~/components/views/theory/TheoryManagementPageView.vue'
const state = bindViewState(useTheoryManagementPage())
</script>
<template><View :state="state" /></template>
