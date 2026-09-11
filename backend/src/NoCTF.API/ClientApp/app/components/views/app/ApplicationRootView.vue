<script setup lang="ts">
import { toRefs } from 'vue'
import type { ApplicationRootViewState } from '~/features/shell/useApplicationRoot'

const viewProps = defineProps<{ state: ApplicationRootViewState }>()
const { isDark, pageTransition, HumanVerificationGate } = toRefs(viewProps.state)
</script>

<template>
  <TooltipProvider :delay-duration="350">
  <NuxtLayout>
    <NuxtPage :transition="pageTransition" />
  </NuxtLayout>
  <component :is="HumanVerificationGate" />
  <Sonner rich-colors close-button position="bottom-right" :visible-toasts="2" :theme="isDark ? 'dark' : 'light'" />
  </TooltipProvider>
</template>
