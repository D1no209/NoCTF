<script setup lang="ts">
import { ArrowRight, CalendarDays, UsersRound } from 'lucide-vue-next'
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
      <span class="competition-card__mode">{{ (props.competition.gameModeType || 'CTF').toUpperCase() }}</span>
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
}

.competition-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  border-bottom: 1px solid var(--v2-line);
  padding: 15px 16px;
}

.competition-card__header h2 {
  display: -webkit-box;
  overflow: hidden;
  margin: 7px 0 0;
  color: var(--v2-text);
  font-size: 16px;
  font-weight: 680;
  letter-spacing: 0;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.competition-card__mode {
  flex: none;
  border: 1px solid var(--v2-line-bright);
  padding: 4px 6px;
  color: var(--v2-primary);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.08em;
}

.competition-card__description {
  display: -webkit-box;
  overflow: hidden;
  margin: 0;
  padding: 14px 16px;
  color: var(--v2-text-muted);
  font-size: 12px;
  line-height: 1.6;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
}

.competition-card__facts {
  display: grid;
  gap: 8px;
  border-top: 1px solid rgb(26 58 103 / 0.7);
  padding: 12px 16px;
}

.competition-card__facts > div { display: grid; grid-template-columns: 66px minmax(0, 1fr); gap: 10px; }
.competition-card__facts dt { display: inline-flex; align-items: center; gap: 5px; color: var(--v2-text-faint); font-size: 10px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; }
.competition-card__facts dd { overflow: hidden; margin: 0; color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; text-overflow: ellipsis; white-space: nowrap; }

.competition-card__footer {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
  border-top: 1px solid var(--v2-line);
  padding: 10px 12px;
}
</style>
