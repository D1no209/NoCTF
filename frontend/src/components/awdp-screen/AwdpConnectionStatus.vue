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
      tone: 'text-amber-200 bg-amber-400/8 border-amber-300/22',
      icon: Radio,
    }
  }
  if (props.status === 'connected') {
    return {
      label: 'Live',
      detail: formatTime(props.lastSyncAt),
      tone: 'text-slate-100 bg-slate-300/8 border-slate-300/20',
      icon: Wifi,
    }
  }
  if (props.status === 'reconnecting') {
    return {
      label: 'Reconnecting',
      detail: `attempt ${Math.max(1, props.reconnectAttempts)}`,
      tone: 'text-amber-100 bg-amber-400/8 border-amber-300/22',
      icon: RefreshCw,
    }
  }
  if (props.status === 'connecting') {
    return {
      label: 'Connecting',
      detail: 'opening stream',
      tone: 'text-slate-200 bg-slate-300/8 border-slate-300/18',
      icon: RefreshCw,
    }
  }
  return {
    label: 'Offline',
    detail: 'snapshot only',
    tone: 'text-rose-100 bg-rose-400/8 border-rose-300/22',
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
