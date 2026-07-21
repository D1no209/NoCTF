<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ArrowRight, Calendar } from 'lucide-vue-next'
import { RouterLink } from 'vue-router'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Panel } from '@/ui-v1/components/ui/panel'

export interface HomeCompetition {
  id: string
  title: string
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
}

defineProps<{
  competition: HomeCompetition
}>()

const { t } = useI18n()

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'approved') return 'default'
  if (s === 'rejected' || s === 'banned') return 'destructive'
  if (s === 'finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <div class="transition-transform duration-200 hover:-translate-y-0.5">
    <Panel class="min-h-[5.5rem]">
      <div class="home-competition-grid">
      <div class="home-competition-main">
        <div class="flex flex-wrap items-center gap-2">
          <h3 class="truncate font-semibold text-foreground">{{ competition.title }}</h3>
          <Badge :variant="statusVariant(competition.status)">{{ competition.status }}</Badge>
          <Badge variant="secondary">{{ competition.gameModeType || 'CTF' }}</Badge>
        </div>
        <div class="flex items-center gap-2 text-xs text-muted-foreground">
          <Calendar class="size-3.5 text-muted-foreground" />
          <span>{{ formatDate(competition.startTime) }} - {{ formatDate(competition.endTime) }}</span>
        </div>
      </div>

      <div class="home-competition-actions">
        <Button size="sm" variant="outline" as-child>
          <RouterLink :to="`/competitions/${competition.id}/register`">
            {{ t('teams.registerForCompetition') }}
          </RouterLink>
        </Button>
        <Button size="sm" as-child>
          <RouterLink :to="`/competitions/${competition.id}`">
            {{ t('competitions.enter') }}
            <ArrowRight class="size-4" />
          </RouterLink>
        </Button>
      </div>
      </div>
    </Panel>
  </div>
</template>

<style scoped>
.home-competition-grid {
  display: grid;
  width: 100%;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
}

.home-competition-main {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 0.4rem;
}

.home-competition-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

@media (max-width: 640px) {
  .home-competition-grid {
    grid-template-columns: 1fr;
  }

  .home-competition-actions {
    justify-content: stretch;
  }

  .home-competition-actions > * {
    flex: 1 1 0;
  }
}
</style>
