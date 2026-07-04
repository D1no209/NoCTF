<script setup lang="ts">
import { AlertTriangle, RefreshCw } from 'lucide-vue-next'

defineProps<{
  message?: string
}>()

defineEmits<{
  retry: []
}>()
</script>

<template>
  <div class="awdp-screen-shell">
    <div class="awdp-screen-bg" />
    <div class="flex min-h-[100dvh] items-center justify-center p-6">
      <section class="awdp-panel max-w-xl p-6 text-center">
        <div class="mx-auto flex size-12 items-center justify-center rounded-lg border border-amber-300/25 bg-amber-300/10 text-amber-100">
          <AlertTriangle class="size-6" />
        </div>
        <h1 class="mt-4 text-xl font-semibold text-slate-50">
          AWDP screen data unavailable
        </h1>
        <p class="mt-2 text-sm text-slate-400">
          {{ message || 'The snapshot endpoint did not return usable data.' }}
        </p>
        <button
          type="button"
          class="mt-5 inline-flex items-center gap-2 rounded-lg border border-cyan-200/25 bg-cyan-200/10 px-4 py-2 text-sm font-semibold text-cyan-50 transition-colors hover:bg-cyan-200/15"
          @click="$emit('retry')"
        >
          <RefreshCw class="size-4" />
          Retry snapshot
        </button>
      </section>
    </div>
  </div>
</template>
