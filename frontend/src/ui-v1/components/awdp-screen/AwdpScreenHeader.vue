<script setup lang="ts">
import type { AwdpScreenConnectionStatus, AwdpScreenSnapshot } from '@/types/awdpScreen'
import { Activity, ChevronLeft, Clock3, Flag, Shield, Swords, Users } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { Button } from '@/ui-v1/components/ui/button'
import { Panel } from '@/ui-v1/components/ui/panel'
import AwdpConnectionStatus from '@/ui-v1/components/awdp-screen/AwdpConnectionStatus.vue'

const props = defineProps<{
  game: AwdpScreenSnapshot['game']
  stats: AwdpScreenSnapshot['stats']
  remainingSeconds: number
  connectionStatus: AwdpScreenConnectionStatus
  reconnectAttempts: number
  lastSyncAt?: string | null
}>()

const { t } = useI18n()
const router = useRouter()

const statusLabel = computed(() => t(`awdpScreen.status.${props.game.status}`))
const phaseLabel = computed(() => t(`awdpScreen.phase.${props.game.phase}`))

const timeText = computed(() => {
  const minutes = Math.floor(props.remainingSeconds / 60)
  const seconds = props.remainingSeconds % 60
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})

function goBack() {
  router.back()
}
</script>

<template>
  <header>
    <Panel variant="default" class="grid grid-cols-1 items-center gap-3 p-3 min-[1281px]:grid-cols-[minmax(0,1.2fr)_minmax(34rem,0.85fr)_auto]">
      <div class="flex min-w-0 items-center gap-3">
        <Button variant="outline" size="sm" class="shrink-0" @click="goBack">
          <ChevronLeft class="size-4" />
          <span class="hidden sm:inline">{{ t('nav.back') }}</span>
        </Button>

        <div class="flex size-10 shrink-0 items-center justify-center rounded-lg border border-[var(--semantic-info-border)] bg-[var(--semantic-info-soft)] text-[var(--semantic-info)]">
          <Activity class="size-5" />
        </div>
        <div class="min-w-0">
          <div class="flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase text-[var(--semantic-info)]">
            <span>{{ t('awdpScreen.headerTitle') }}</span>
            <span class="rounded border border-[var(--semantic-info-border)] px-2 py-0.5 text-[var(--semantic-info)]">{{ statusLabel }}</span>
            <span class="rounded border border-[var(--awdp-border)] px-2 py-0.5 text-[var(--awdp-text-muted)]">{{ phaseLabel }}</span>
          </div>
          <h1 class="mt-1 truncate text-xl font-semibold text-[var(--awdp-text)]">
            {{ game.title }}
          </h1>
        </div>
      </div>

      <div class="grid grid-cols-4 gap-2">
        <div class="grid min-h-12 grid-cols-[auto_1fr] grid-rows-2 items-center gap-x-2 rounded-lg border border-[var(--awdp-border)] bg-[var(--awdp-surface)] px-[0.65rem] py-[0.42rem]">
          <Clock3 class="row-span-2 size-4 text-[var(--semantic-info)]" />
          <span class="text-[0.68rem] font-bold uppercase text-[var(--awdp-text-muted)]">{{ t('awdpScreen.metrics.round') }}</span>
          <strong class="font-mono text-base text-[var(--awdp-text)]">{{ game.currentRound }} / {{ game.totalRounds }}</strong>
        </div>
        <div class="grid min-h-12 grid-cols-[auto_1fr] grid-rows-2 items-center gap-x-2 rounded-lg border border-[var(--awdp-border)] bg-[var(--awdp-surface)] px-[0.65rem] py-[0.42rem]">
          <Users class="row-span-2 size-4 text-[var(--semantic-info)]" />
          <span class="text-[0.68rem] font-bold uppercase text-[var(--awdp-text-muted)]">{{ t('awdpScreen.metrics.teams') }}</span>
          <strong class="font-mono text-base text-[var(--awdp-text)]">{{ stats.teamCount }}</strong>
        </div>
        <div class="grid min-h-12 grid-cols-[auto_1fr] grid-rows-2 items-center gap-x-2 rounded-lg border border-[var(--awdp-border)] bg-[var(--awdp-surface)] px-[0.65rem] py-[0.42rem]">
          <Flag class="row-span-2 size-4 text-[var(--semantic-info)]" />
          <span class="text-[0.68rem] font-bold uppercase text-[var(--awdp-text-muted)]">{{ t('awdpScreen.metrics.challenges') }}</span>
          <strong class="font-mono text-base text-[var(--awdp-text)]">{{ stats.challengeCount }}</strong>
        </div>
        <div class="grid min-h-12 grid-cols-[auto_1fr] grid-rows-2 items-center gap-x-2 rounded-lg border border-[var(--awdp-border)] bg-[var(--awdp-surface)] px-[0.65rem] py-[0.42rem]">
          <Swords class="row-span-2 size-4 text-[var(--semantic-info)]" />
          <span class="text-[0.68rem] font-bold uppercase text-[var(--awdp-text-muted)]">{{ t('awdpScreen.metrics.attack') }}</span>
          <strong class="font-mono text-base text-[var(--awdp-text)]">{{ stats.totalAttackCount }}</strong>
        </div>
      </div>

      <div class="flex items-center justify-end gap-3">
        <div class="rounded-lg border border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] px-3 py-1.5 text-right">
          <div class="flex items-center justify-end gap-2 text-[11px] font-semibold uppercase text-[var(--semantic-warning)]">
            <Shield class="size-3.5" />
            {{ t('awdpScreen.metrics.roundTimer') }}
          </div>
          <div class="font-mono text-2xl font-semibold tabular-nums text-[var(--semantic-warning)]">
            {{ timeText }}
          </div>
        </div>
        <AwdpConnectionStatus
          :status="connectionStatus"
          :reconnect-attempts="reconnectAttempts"
          :last-sync-at="lastSyncAt"
        />
      </div>
    </Panel>
  </header>
</template>
