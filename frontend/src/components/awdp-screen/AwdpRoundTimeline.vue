<script setup lang="ts">
import type { AwdpRoundStat } from '@/types/awdpScreen'
import { Timer } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Panel } from '@/components/ui/panel'
import { Separator } from '@/components/ui/separator'
import AwdpRoundStackedBar from './AwdpRoundStackedBar.vue'

defineProps<{
  rounds: AwdpRoundStat[]
  currentRound: number
}>()

const { t } = useI18n()

const legend = [
  { key: 'attackSuccessCount', color: 'bg-cyan-500', label: t('awdpScreen.timeline.attackSuccess') },
  { key: 'attackFailCount', color: 'bg-amber-500', label: t('awdpScreen.timeline.attackFailed') },
  { key: 'defenseSuccessCount', color: 'bg-emerald-500', label: t('awdpScreen.timeline.defenseSuccess') },
  { key: 'defenseFailCount', color: 'bg-rose-500', label: t('awdpScreen.timeline.defenseFailed') },
]
</script>

<template>
  <Panel variant="default" class="flex min-h-[16rem] flex-col">
    <div class="flex items-center justify-between px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-slate-900">
          {{ t('awdpScreen.timeline.title') }}
        </h2>
        <p class="text-xs text-slate-500">
          {{ t('awdpScreen.timeline.subtitle') }}
        </p>
      </div>
      <Timer class="size-5 text-cyan-600" />
    </div>

    <Separator class="bg-slate-300/40" />

    <div v-if="rounds.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      {{ t('awdpScreen.timeline.empty') }}
    </div>

    <div v-else class="flex min-h-0 flex-1 flex-col">
      <div class="flex-1 overflow-hidden p-3">
        <AwdpRoundStackedBar :rounds="rounds" :current-round="currentRound" />
      </div>

      <div class="flex flex-wrap items-center justify-end gap-3 border-t border-slate-300/40 px-4 py-2">
        <div
          v-for="item in legend"
          :key="item.key"
          class="flex items-center gap-1.5 text-[10px] font-semibold text-slate-600"
        >
          <span class="size-2.5 rounded-sm" :class="item.color" />
          <span>{{ item.label }}</span>
        </div>
      </div>
    </div>
  </Panel>
</template>
