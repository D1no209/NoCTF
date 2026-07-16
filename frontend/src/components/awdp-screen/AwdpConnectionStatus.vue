<script setup lang="ts">
import type { AwdpScreenConnectionStatus } from '@/types/awdpScreen'
import { RefreshCw, Wifi, WifiOff } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  status: AwdpScreenConnectionStatus
  reconnectAttempts: number
  lastSyncAt?: string | null
}>()

const { t } = useI18n()

const statusView = computed(() => {
  if (props.status === 'connected') {
    return {
      label: t('awdpScreen.connection.live'),
      detail: formatTime(props.lastSyncAt),
      tone: 'text-cyan-700 bg-cyan-100/50 border-cyan-300/50',
      icon: Wifi,
    }
  }
  if (props.status === 'reconnecting') {
    return {
      label: t('awdpScreen.connection.reconnecting'),
      detail: t('awdpScreen.connection.attempt', { count: Math.max(1, props.reconnectAttempts) }),
      tone: 'text-orange-700 bg-orange-100/50 border-orange-300/50',
      icon: RefreshCw,
    }
  }
  if (props.status === 'connecting') {
    return {
      label: t('awdpScreen.connection.connecting'),
      detail: t('awdpScreen.connection.openingStream'),
      tone: 'text-slate-700 bg-slate-100/50 border-slate-300/40',
      icon: RefreshCw,
    }
  }
  return {
    label: t('awdpScreen.connection.offline'),
    detail: t('awdpScreen.connection.snapshotOnly'),
    tone: 'text-rose-700 bg-rose-100/50 border-rose-300/50',
    icon: WifiOff,
  }
})

function formatTime(value?: string | null) {
  if (!value)
    return t('awdpScreen.connection.synced')
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <div
    class="inline-flex min-w-[11rem] items-center justify-between gap-3 rounded-lg border px-3 py-2 text-xs"
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
