<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { Badge } from '@/components/ui/badge'
import { Panel } from '@/components/ui/panel'

export interface HomeTeam {
  id: string
  name: string
  competitionId: string
  competitionTitle: string
  registrationStatus: string
}

defineProps<{
  team: HomeTeam
}>()

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'approved') return 'default'
  if (s === 'rejected' || s === 'banned') return 'destructive'
  return 'secondary'
}
</script>

<template>
  <div class="transition-transform duration-200 hover:-translate-y-0.5">
    <Panel class="min-h-[4.5rem]">
    <RouterLink
      :to="`/competitions/${team.competitionId}/register`"
      class="flex h-full min-h-[inherit] items-center justify-between gap-4 px-4 py-3"
    >
      <div class="min-w-0">
        <div class="truncate font-semibold text-black">{{ team.name }}</div>
        <div class="mt-1 truncate text-xs text-zinc-600">{{ team.competitionTitle }}</div>
      </div>
      <Badge :variant="statusVariant(team.registrationStatus)">{{ team.registrationStatus }}</Badge>
    </RouterLink>
    </Panel>
  </div>
</template>
