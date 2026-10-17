<script setup lang="ts">
import type { MfaStepUpViewState } from './useMfaStepUp'
import MfaStepUpView from '~/components/views/authentication/MfaStepUpView.vue'

defineProps<{ state: MfaStepUpViewState }>()
</script>

<template>
  <MfaStepUpView :state="state" />
</template>
