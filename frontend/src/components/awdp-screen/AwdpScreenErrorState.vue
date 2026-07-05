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
        <div class="awdp-error-icon mx-auto flex size-12 items-center justify-center rounded-lg border">
          <AlertTriangle class="size-6" />
        </div>
        <h1 class="mt-4 text-xl font-semibold text-sidebar-foreground">
          AWDP screen data unavailable
        </h1>
        <p class="mt-2 text-sm text-sidebar-foreground/60">
          {{ message || 'The snapshot endpoint did not return usable data.' }}
        </p>
        <button
          type="button"
          class="awdp-error-action mt-5 inline-flex items-center gap-2 rounded-lg border px-4 py-2 text-sm font-semibold transition-colors"
          @click="$emit('retry')"
        >
          <RefreshCw class="size-4" />
          Retry snapshot
        </button>
      </section>
    </div>
  </div>
</template>

<style scoped>
.awdp-error-icon {
  border-color: color-mix(in oklch, var(--awdp-warn) 30%, transparent);
  background: color-mix(in oklch, var(--awdp-warn) 10%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 90%, var(--awdp-warn));
}

.awdp-error-action {
  border-color: color-mix(in oklch, var(--awdp-break) 28%, transparent);
  background: color-mix(in oklch, var(--awdp-break) 10%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 92%, var(--awdp-break));
}

.awdp-error-action:hover {
  background: color-mix(in oklch, var(--awdp-break) 14%, transparent);
}
</style>
