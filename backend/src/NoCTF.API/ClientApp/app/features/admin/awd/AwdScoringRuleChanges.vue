<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAwdScoringRuleChanges, type AwdScoringRuleChangesProps } from './useAwdScoringRuleChanges'
import View from '~/components/views/admin/awd/AwdScoringRuleChangesView.vue'
const props = defineProps<AwdScoringRuleChangesProps>()
const state = bindViewState(useAwdScoringRuleChanges(props))
</script>
<template><View :state="state" /></template>
