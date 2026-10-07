<script setup lang="ts">
import { bindViewState } from '../../shared/view-state'
import { useStaffWorkbench } from './useStaffWorkbench'
import View from '../../../components/views/competitions/StaffWorkbenchView.vue'
const state = bindViewState(useStaffWorkbench())
</script>
<template><View :state="state" /></template>
