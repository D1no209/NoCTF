<script setup lang="ts">
import { bindViewState } from '../../shared/view-state'
import { useStaffWebhooks } from './useStaffWebhooks'
import View from '../../../components/views/competitions/StaffWebhooksView.vue'
const state = bindViewState(useStaffWebhooks())
</script>
<template><View :state="state" /></template>
