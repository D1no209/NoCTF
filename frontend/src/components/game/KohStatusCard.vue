<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'

export interface KohControlEntry {
  teamId: string | null
  teamName: string | null
  startTime: string
  endTime: string | null
}

export interface KohChallengeStatus {
  challengeId: string
  challengeName: string
  controllerTeamId: string | null
  controllerTeamName: string | null
  controlStartTime: string | null
  controlDurationSeconds: number
  history: KohControlEntry[]
}

const props = defineProps<{
  status: KohChallengeStatus
}>()

const { t } = useI18n()

const controllerLabel = computed(() =>
  props.status.controllerTeamName ?? t('koh.uncontested')
)

const durationLabel = computed(() => {
  const s = props.status.controlDurationSeconds
  if (s <= 0) return '-'
  const m = Math.floor(s / 60)
  const sec = s % 60
  return m > 0 ? `${m}m ${sec}s` : `${sec}s`
})

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>

<template>
  <Card class="noctf-live-card flex flex-col">
    <CardHeader class="pb-2">
      <div class="flex items-center justify-between gap-2">
        <CardTitle class="text-sm font-semibold truncate">{{ status.challengeName }}</CardTitle>
        <Badge
          :variant="status.controllerTeamId ? 'default' : 'outline'"
          class="shrink-0 text-xs"
        >
          {{ status.controllerTeamId ? t('koh.controlled') : t('koh.uncontested') }}
        </Badge>
      </div>
    </CardHeader>
    <CardContent class="space-y-3 flex-1">
      <!-- Current controller -->
      <div class="space-y-0.5 rounded-md border bg-muted/30 px-3 py-2">
        <p class="text-xs text-muted-foreground">{{ t('koh.controller') }}</p>
        <p class="font-medium text-sm">{{ controllerLabel }}</p>
        <p class="text-xs text-muted-foreground font-mono">{{ durationLabel }}</p>
      </div>

      <!-- History -->
      <div v-if="status.history.length > 0">
        <p class="text-xs font-medium text-muted-foreground mb-1.5">{{ t('koh.history') }}</p>
        <ul class="noctf-scrollbar max-h-32 space-y-1 overflow-y-auto">
          <li
            v-for="(entry, i) in status.history"
            :key="i"
            class="flex items-center justify-between text-xs gap-2"
          >
            <span class="font-medium truncate">{{ entry.teamName ?? t('koh.uncontested') }}</span>
            <span class="text-muted-foreground font-mono shrink-0">{{ formatTime(entry.startTime) }}</span>
          </li>
        </ul>
      </div>
      <p v-else class="text-xs text-muted-foreground italic">{{ t('koh.noHistory') }}</p>
    </CardContent>
  </Card>
</template>
