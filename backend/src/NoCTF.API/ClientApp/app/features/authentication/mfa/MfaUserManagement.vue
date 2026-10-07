<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useMfaUserManagement } from './useMfaUserManagement'
import View from '~/components/views/authentication/MfaUserManagementView.vue'
const props = defineProps<{ userId: string; required: boolean }>()
const state = bindViewState(useMfaUserManagement(props))
</script>
<template><View :state="state" /></template>
