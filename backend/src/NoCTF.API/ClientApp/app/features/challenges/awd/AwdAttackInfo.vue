<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAwdAttackInfo, type AwdAttackInfoProps } from './useAwdAttackInfo'
import View from '~/components/views/challenges/awd/AwdAttackInfoView.vue'
const props = defineProps<AwdAttackInfoProps>()
const emit = defineEmits<{ close: [] }>()
const state = bindViewState(useAwdAttackInfo(props, emit))
</script>

<template><View :state="state" /></template>
