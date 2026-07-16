<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

export interface ServiceStatus {
  teamId: string
  teamName: string
  challengeId: string
  challengeName: string
  status: 'healthy' | 'down' | 'unknown'
}

const props = defineProps<{
  services: ServiceStatus[]
}>()

const { t } = useI18n()

// Group by challenge for a matrix-style view
const challenges = computed(() => {
  const map = new Map<string, string>()
  for (const s of props.services) {
    map.set(s.challengeId, s.challengeName)
  }
  return Array.from(map.entries()).map(([id, name]) => ({ id, name }))
})

const teams = computed(() => {
  const map = new Map<string, string>()
  for (const s of props.services) {
    map.set(s.teamId, s.teamName)
  }
  return Array.from(map.entries()).map(([id, name]) => ({ id, name }))
})

function getStatus(teamId: string, challengeId: string): 'healthy' | 'down' | 'unknown' {
  return (
    props.services.find((s) => s.teamId === teamId && s.challengeId === challengeId)?.status ??
    'unknown'
  )
}

function statusClass(status: 'healthy' | 'down' | 'unknown') {
  if (status === 'healthy') return 'bg-emerald-500'
  if (status === 'down') return 'bg-destructive'
  return 'bg-muted'
}

function statusLabel(status: 'healthy' | 'down' | 'unknown') {
  if (status === 'healthy') return t('awd.statusUp')
  if (status === 'down') return t('awd.statusDown')
  return t('awd.statusUnknown')
}
</script>

<template>
  <Card class="rounded-xl">
    <CardHeader>
      <CardTitle class="text-base">{{ t('awd.serviceStatus') }}</CardTitle>
    </CardHeader>
    <CardContent>
      <div v-if="services.length === 0" class="text-sm text-muted-foreground">
        {{ t('awd.noServices') }}
      </div>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead class="py-2 pr-2 text-left text-xs">
              {{ t('awd.teamHeader') }}
            </TableHead>
            <TableHead
              v-for="ch in challenges"
              :key="ch.id"
              class="py-2 px-1 text-center text-xs truncate max-w-[60px]"
              :title="ch.name"
            >
              {{ ch.name }}
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="team in teams" :key="team.id">
            <TableCell class="py-1.5 pr-2 truncate max-w-[80px]" :title="team.name">
              {{ team.name }}
            </TableCell>
            <TableCell
              v-for="ch in challenges"
              :key="ch.id"
              class="py-1.5 px-1 text-center"
            >
              <span
                class="inline-flex h-5 w-10 items-center justify-center border-2 border-border text-[10px] font-bold text-white"
                :class="statusClass(getStatus(team.id, ch.id))"
                :title="getStatus(team.id, ch.id)"
              >
                {{ statusLabel(getStatus(team.id, ch.id)) }}
              </span>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>
