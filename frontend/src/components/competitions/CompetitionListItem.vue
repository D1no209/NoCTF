<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { ArrowRight, Calendar, Plug, Shield, Target, Trophy, Users } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import type { Component } from 'vue'

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
  registeredTeamCount?: number | null
}

const props = defineProps<{
  competition: Competition
}>()

const { t } = useI18n()

function modeLabel(mode?: string | null) {
  return (mode || 'CTF').toUpperCase()
}

const modeMeta = computed(() => {
  const key = (props.competition.gameModeType || 'ctf').toLowerCase()
  const map: Record<string, { bg: string, fg: string, icon: Component }> = {
    ctf: { bg: 'bg-primary', fg: 'text-primary-foreground', icon: Trophy },
    awd: { bg: 'bg-muted', fg: 'text-foreground', icon: Shield },
    awdp: { bg: 'bg-accent', fg: 'text-accent-foreground', icon: Plug },
    koh: { bg: 'bg-secondary', fg: 'text-secondary-foreground', icon: Target },
  }
  return map[key] || { bg: 'bg-muted', fg: 'text-foreground', icon: Trophy }
})

function statusLabel(status: string) {
  const key = status.toLowerCase()
  return t(`competitions.status.${key}`, status)
}

function statusToneClass(status: string) {
  const key = status.toLowerCase()
  if (key === 'running' || key === 'active')
    return 'border-border bg-primary text-primary-foreground'
  if (key === 'published' || key === 'upcoming')
    return 'border-border bg-secondary text-secondary-foreground'
  if (key === 'paused' || key === 'pending')
    return 'border-border bg-accent text-accent-foreground'
  if (key === 'finished' || key === 'ended')
    return 'border-border bg-muted text-muted-foreground'
  return 'border-border bg-card text-card-foreground'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <Card
    :decorated="false"
    class="group relative flex h-full flex-col overflow-hidden py-0 transition-all duration-200 ease-out hover:-translate-y-1 hover:shadow-float"
  >
    <!-- Top grip ridge -->
    <div
      class="absolute inset-x-0 top-0 z-10 h-4 border-y-2 border-border bg-muted"
      :style="{ backgroundImage: 'repeating-linear-gradient(90deg, rgb(0 0 0 / 0.14) 0px, rgb(0 0 0 / 0.14) 2px, transparent 2px, transparent 5px)' }"
      aria-hidden="true"
    />

    <!-- Label / poster area -->
    <Panel class="mx-3 mt-4 mb-4 flex flex-1 flex-col p-0" border="default">
      <div
        class="relative flex h-28 flex-col items-center justify-center gap-2 overflow-hidden"
        :class="[modeMeta.bg, modeMeta.fg]"
      >
        <div
          class="absolute inset-0"
          :style="{ backgroundImage: 'repeating-linear-gradient(45deg, rgb(0 0 0 / 0.12) 0px, rgb(0 0 0 / 0.12) 2px, transparent 2px, transparent 10px)' }"
          aria-hidden="true"
        />
        <component :is="modeMeta.icon" class="relative z-10 size-10" />
        <span class="relative z-10 text-xs font-bold uppercase tracking-[0.2em] opacity-80">{{ modeLabel(competition.gameModeType) }}</span>
      </div>

      <div class="flex flex-1 flex-col gap-3 p-4">
        <div class="flex flex-wrap items-center gap-2">
          <Badge :class="statusToneClass(competition.status)">{{ statusLabel(competition.status) }}</Badge>
        </div>

        <div class="space-y-1">
          <h2 class="line-clamp-2 text-lg font-bold leading-tight text-foreground">{{ competition.title }}</h2>
          <p v-if="competition.description" class="line-clamp-2 text-sm leading-relaxed text-muted-foreground">{{ competition.description }}</p>
        </div>

        <div class="mt-auto space-y-3">
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
            <div class="flex items-center gap-1.5">
              <Calendar class="size-3.5 text-muted-foreground" />
              <span>{{ formatDate(competition.startTime) }} ~ {{ formatDate(competition.endTime) }}</span>
            </div>
            <div class="flex items-center gap-1.5">
              <Users class="size-3.5 text-muted-foreground" />
              <span>{{ competition.registeredTeamCount ?? 0 }} {{ t('competitions.registeredTeams') }}</span>
            </div>
          </div>

          <div class="flex gap-2">
            <Button variant="outline" class="flex-1" as-child>
              <RouterLink :to="`/competitions/${competition.id}/register`">{{ t('teams.registerForCompetition') }}</RouterLink>
            </Button>
            <Button class="flex-1" as-child>
              <RouterLink :to="`/competitions/${competition.id}`">
                {{ t('competitions.enter') }}
                <ArrowRight class="size-4" />
              </RouterLink>
            </Button>
          </div>
        </div>
      </div>
    </Panel>

    <!-- Bottom grip ridge -->
    <div
      class="absolute inset-x-0 bottom-0 z-10 h-4 border-y-2 border-border bg-muted"
      :style="{ backgroundImage: 'repeating-linear-gradient(90deg, rgb(0 0 0 / 0.14) 0px, rgb(0 0 0 / 0.14) 2px, transparent 2px, transparent 5px)' }"
      aria-hidden="true"
    />
  </Card>
</template>
