<script setup lang="ts">
import { ArrowRight, CalendarDays, UsersRound } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandCompetition {
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
  competition: CommandCompetition
}>()

defineEmits<{
  open: [id: string]
  register: [id: string]
}>()

function statusTone(status: string) {
  const value = status.toLowerCase()
  if (value === 'running' || value === 'active')
    return 'success'
  if (value === 'draft')
    return 'warning'
  if (value === 'finished' || value === 'ended')
    return 'danger'
  return 'info'
}

function formatDateRange(startTime: string, endTime: string) {
  const formatter = new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
  return `${formatter.format(new Date(startTime))} - ${formatter.format(new Date(endTime))}`
}
</script>

<template>
  <CommandPanel class="competition-card" :tone="statusTone(props.competition.status) === 'success' ? 'signal' : 'default'">
    <header class="competition-card__header">
      <div>
        <CommandSignal :label="props.competition.status" :tone="statusTone(props.competition.status)" />
        <h2>{{ props.competition.title }}</h2>
      </div>
      <CommandBadge :label="(props.competition.gameModeType || 'CTF').toUpperCase()" tone="primary" />
    </header>

    <p class="competition-card__description">
      {{ props.competition.description || 'No competition brief has been published.' }}
    </p>

    <dl class="competition-card__facts">
      <div>
        <dt><CalendarDays class="size-3.5" /> Window</dt>
        <dd>{{ formatDateRange(props.competition.startTime, props.competition.endTime) }}</dd>
      </div>
      <div>
        <dt><UsersRound class="size-3.5" /> Teams</dt>
        <dd>{{ props.competition.registeredTeamCount ?? 0 }} registered</dd>
      </div>
    </dl>

    <footer class="competition-card__footer">
      <CommandButton label="Register" tone="ghost" @click="$emit('register', props.competition.id)" />
      <CommandButton label="Open" @click="$emit('open', props.competition.id)">
        <template #icon>
          <ArrowRight class="size-4" />
        </template>
      </CommandButton>
    </footer>
  </CommandPanel>
</template>

<style scoped>
.competition-card {
  display: grid;
  min-height: 286px;
  grid-template-rows: auto 1fr auto auto;
  gap: 4px;
}

.competition-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding: 18px 18px 0;
}

.competition-card__header h2 {
  display: -webkit-box;
  overflow: hidden;
  margin: 8px 0 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.competition-card__description {
  display: -webkit-box;
  overflow: hidden;
  margin: 0;
  padding: 12px 18px;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.6;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
}

.competition-card__facts {
  display: grid;
  gap: 8px;
  margin: 0 18px;
  border-radius: 12px;
  padding: 12px 14px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}

.competition-card__facts > div { display: grid; grid-template-columns: 66px minmax(0, 1fr); gap: 10px; }
.competition-card__facts dt { display: inline-flex; align-items: center; gap: 5px; color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.03em; }
.competition-card__facts dd { overflow: hidden; margin: 0; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }

.competition-card__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 14px 18px 18px;
}
</style>
