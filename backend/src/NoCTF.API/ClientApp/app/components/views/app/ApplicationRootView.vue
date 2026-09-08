<script setup lang="ts">
import { toRefs } from 'vue'
import type { ApplicationRootViewState } from '~/features/shell/useApplicationRoot'

const viewProps = defineProps<{ state: ApplicationRootViewState }>()
const { isDark } = toRefs(viewProps.state)
</script>

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <Sonner rich-colors close-button position="top-right" :theme="isDark ? 'dark' : 'light'" />
</template>
