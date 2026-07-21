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
      tone: 'text-[var(--semantic-info)] bg-[var(--semantic-info-soft)] border-[var(--semantic-info-border)]',
      icon: Wifi,
    }
  }
  if (props.status === 'reconnecting') {
    return {
      label: t('awdpScreen.connection.reconnecting'),
      detail: t('awdpScreen.connection.attempt', { count: Math.max(1, props.reconnectAttempts) }),
      tone: 'text-[var(--semantic-warning)] bg-[var(--semantic-warning-soft)] border-[var(--semantic-warning-border)]',
      icon: RefreshCw,
    }
  }
  if (props.status === 'connecting') {
    return {
      label: t('awdpScreen.connection.connecting'),
      detail: t('awdpScreen.connection.openingStream'),
      tone: 'text-[var(--semantic-neutral)] bg-[var(--semantic-neutral-soft)] border-[var(--awdp-border)]',
      icon: RefreshCw,
    }
  }
  return {
    label: t('awdpScreen.connection.offline'),
    detail: t('awdpScreen.connection.snapshotOnly'),
    tone: 'text-[var(--semantic-danger)] bg-[var(--semantic-danger-soft)] border-[var(--semantic-danger-border)]',
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
