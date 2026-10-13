<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAwdPublicRules, type AwdPublicRulesProps } from './useAwdPublicRules'
import View from '~/components/views/challenges/awd/AwdPublicRulesView.vue'
const props = defineProps<AwdPublicRulesProps>()
const state = bindViewState(useAwdPublicRules(props))
</script>

<template><View :state="state" /></template>
