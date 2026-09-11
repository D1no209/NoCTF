<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useHumanVerificationGate } from './useHumanVerification'
import View from '~/components/views/security/HumanVerificationGateView.vue'

const state = bindViewState(useHumanVerificationGate())
</script>

<template>
  <View :state="state" />
</template>
