<script setup lang="ts">
import { computed } from 'vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'

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
  <Card class="noctf-live-card flex h-full flex-col">
    <CardHeader>
      <CardTitle class="text-base flex items-center gap-2">
        {{ $t('awd.attackLog') }}
        <Badge variant="secondary" class="font-mono text-xs">{{ logs.length }}</Badge>
      </CardTitle>
    </CardHeader>
    <CardContent class="flex-1 overflow-hidden p-0">
      <div class="noctf-scrollbar h-full max-h-[500px] space-y-1.5 overflow-y-auto px-4 pb-4">
        <div v-if="logs.length === 0" class="text-sm text-muted-foreground pt-2">
          {{ $t('awd.noAttacks') }}
        </div>
        <div
          v-for="(log, i) in sortedLogs"
          :key="i"
          class="log-entry rounded-md border border-border/40 bg-muted/40 px-2 py-1.5 text-xs"
        >
          <div class="flex items-center justify-between gap-2 mb-0.5">
            <span class="font-semibold text-foreground truncate">{{ log.attackerTeamName }}</span>
            <span class="text-muted-foreground shrink-0">R{{ log.roundNumber }}</span>
          </div>
          <div class="flex items-center gap-1 text-muted-foreground">
            <span class="text-red-500">-&gt;</span>
            <span class="truncate">{{ log.victimTeamName }}</span>
            <span class="mx-1">·</span>
            <span class="truncate text-blue-400">{{ log.challengeName }}</span>
          </div>
          <div class="text-[10px] text-muted-foreground/60 mt-0.5">
            {{ formatTime(log.timestamp) }}
          </div>
        </div>
      </div>
    </CardContent>
  </Card>
</template>

<style scoped>
.log-entry {
  animation: slideIn 0.25s ease-out;
}

@keyframes slideIn {
  from {
    opacity: 0;
    transform: translateX(8px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}
</style>
