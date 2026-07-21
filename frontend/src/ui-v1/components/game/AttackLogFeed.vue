<script setup lang="ts">
import { computed } from 'vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Badge } from '@/ui-v1/components/ui/badge'

export interface AttackLogDto {
  attackerTeamId: string
  attackerTeamName: string
  victimTeamId: string
  victimTeamName: string
  challengeId: string
  challengeName: string
  roundNumber: number
  timestamp: string
}

const props = defineProps<{
  logs: AttackLogDto[]
}>()

const sortedLogs = computed(() => [...props.logs].reverse())

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>

<template>
  <Card class="flex h-full flex-col rounded-xl">
    <CardHeader>
      <CardTitle class="text-base flex items-center gap-2">
        {{ $t('awd.attackLog') }}
        <Badge variant="secondary" class="font-mono text-xs">{{ logs.length }}</Badge>
      </CardTitle>
    </CardHeader>
    <CardContent class="flex-1 overflow-hidden p-0">
      <div class="h-full max-h-[500px] space-y-1.5 overflow-y-auto px-4 pb-4">
        <div v-if="logs.length === 0" class="text-sm text-muted-foreground pt-2">
          {{ $t('awd.noAttacks') }}
        </div>
        <Card
          v-for="(log, i) in sortedLogs"
          :key="i"
          class="p-2 gap-1"
        >
          <div class="flex items-center justify-between gap-2">
            <span class="font-semibold text-foreground text-xs truncate">{{ log.attackerTeamName }}</span>
            <span class="text-muted-foreground shrink-0 text-xs">R{{ log.roundNumber }}</span>
          </div>
          <div class="flex items-center gap-1 text-xs text-muted-foreground">
            <span class="text-destructive">-&gt;</span>
            <span class="truncate">{{ log.victimTeamName }}</span>
            <span class="mx-1">·</span>
            <span class="truncate text-primary">{{ log.challengeName }}</span>
          </div>
          <div class="text-[10px] text-muted-foreground/60">
            {{ formatTime(log.timestamp) }}
          </div>
        </Card>
      </div>
    </CardContent>
  </Card>
</template>
