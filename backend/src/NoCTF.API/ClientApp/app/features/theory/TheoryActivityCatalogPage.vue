<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTheoryActivityCatalogPage } from './useTheoryActivityCatalogPage'
import View from '~/components/views/theory/TheoryActivityCatalogPageView.vue'
const state = bindViewState(useTheoryActivityCatalogPage())
</script>
<template><View :state="state" /></template>
