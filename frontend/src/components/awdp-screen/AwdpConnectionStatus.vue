<script setup lang="ts">
import type { AwdpScreenConnectionStatus } from '@/types/awdpScreen'
import { Radio, RefreshCw, Wifi, WifiOff } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{
  status: AwdpScreenConnectionStatus
  usingMock: boolean
  reconnectAttempts: number
  lastSyncAt?: string | null
}>()

const statusView = computed(() => {
  if (props.usingMock || props.status === 'mock') {
    return {
      label: 'Mock feed',
      detail: 'central mock data',
      tone: 'connection-warning',
      icon: Radio,
    }
  }
  if (props.status === 'connected') {
    return {
      label: 'Live',
      detail: formatTime(props.lastSyncAt),
      tone: 'connection-live',
      icon: Wifi,
    }
  }
  if (props.status === 'reconnecting') {
    return {
      label: 'Reconnecting',
      detail: `attempt ${Math.max(1, props.reconnectAttempts)}`,
      tone: 'connection-warning',
      icon: RefreshCw,
    }
  }
  if (props.status === 'connecting') {
    return {
      label: 'Connecting',
      detail: 'opening stream',
      tone: 'connection-neutral',
      icon: RefreshCw,
    }
  }
  return {
    label: 'Offline',
    detail: 'snapshot only',
    tone: 'connection-danger',
    icon: WifiOff,
  }
})

function formatTime(value?: string | null) {
  if (!value)
    return 'synced'
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <div
    class="inline-flex min-w-[11rem] items-center justify-between gap-3 rounded-md border px-3 py-2 text-xs"
    :class="statusView.tone"
  >
    <div class="flex min-w-0 items-center gap-2">
      <component
        :is="statusView.icon"
        class="size-4 shrink-0"
        :class="status === 'connecting' || status === 'reconnecting' ? 'animate-spin' : ''"
      />
      <span class="truncate font-semibold">{{ statusView.label }}</span>
    </div>
    <span class="shrink-0 font-mono text-[11px] opacity-75">{{ statusView.detail }}</span>
  </div>
</template>

<style scoped>
.connection-live {
  border-color: color-mix(in oklch, var(--awdp-fix) 26%, transparent);
  background: color-mix(in oklch, var(--awdp-fix) 8%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 92%, var(--awdp-fix));
}

.connection-warning {
  border-color: color-mix(in oklch, var(--awdp-warn) 28%, transparent);
  background: color-mix(in oklch, var(--awdp-warn) 9%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 88%, var(--awdp-warn));
}

.connection-danger {
  border-color: color-mix(in oklch, var(--awdp-error) 28%, transparent);
  background: color-mix(in oklch, var(--awdp-error) 9%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 88%, var(--awdp-error));
}

.connection-neutral {
  border-color: color-mix(in oklch, var(--sidebar-foreground) 20%, transparent);
  background: color-mix(in oklch, var(--sidebar-foreground) 8%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 82%, transparent);
}
</style>
