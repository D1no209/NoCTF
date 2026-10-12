<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTheoryBankPage } from './useTheoryBankPage'
import View from '~/components/views/theory/TheoryBankPageView.vue'
const state = bindViewState(useTheoryBankPage())
</script>
<template><View :state="state" /></template>
