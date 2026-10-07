<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useMfaAccountSecurity } from './useMfaAccountSecurity'
import View from '~/components/views/authentication/MfaAccountSecurityView.vue'
const state = bindViewState(useMfaAccountSecurity())
</script>
<template><View :state="state" /></template>
