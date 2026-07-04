<script setup lang="ts">
import AppErrorBoundary from '@/components/AppErrorBoundary.vue'
import { Toaster } from '@/components/ui/sonner'
</script>

<template>
  <AppErrorBoundary>
    <RouterView />
  </AppErrorBoundary>
  <Toaster position="top-right" rich-colors close-button />
</template>
