<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { usePasskeyAccountSecurity } from './usePasskeyAccountSecurity'
import View from '~/components/views/authentication/PasskeyAccountSecurityView.vue'
const state = bindViewState(usePasskeyAccountSecurity())
</script>
<template><View :state="state" /></template>
