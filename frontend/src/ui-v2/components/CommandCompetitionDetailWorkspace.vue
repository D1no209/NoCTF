<script setup lang="ts">
import { ArrowLeft, CalendarDays, Crosshair, Gauge, MonitorPlay, Network, Radar, RefreshCw, Trophy, UserPlus, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandChallengeGrid, { type CommandChallenge } from './CommandChallengeGrid.vue'
import CommandLeaderboardTable, { type CommandLeaderboardEntry } from './CommandLeaderboardTable.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandCompetitionDetail {
  id: string
  title: string
  description: string | null
  status: string
  gameModeType: string
  startTime: string | null
  endTime: string | null
  maxTeamMembers: number | null
  tracksEnabled: boolean
  trackNames: string[]
}

type DetailState = 'invalid' | 'loading' | 'error' | 'ready'

const props = defineProps<{
  state: DetailState
  errorMessage?: string
  competition?: CommandCompetitionDetail
  challenges: CommandChallenge[]
  leaderboard: CommandLeaderboardEntry[]
  loadingChallenges: boolean
  loadingLeaderboard: boolean
  challengeError: boolean
  leaderboardError: boolean
}>()

defineEmits<{
  back: []
  register: []
  retry: []
  'open-awdp-screen': []
  'open-awd-dashboard': []
  'open-koh-dashboard': []
  'open-penetration-dashboard': []
  'select-challenge': [challenge: CommandChallenge]
}>()

const isAwdp = computed(() => props.competition?.gameModeType.toLowerCase() === 'awdp')
const isAwd = computed(() => props.competition?.gameModeType.toLowerCase() === 'awd')
const isKoh = computed(() => props.competition?.gameModeType.toLowerCase() === 'koh')
const isPenetration = computed(() => props.competition?.gameModeType.toLowerCase() === 'penetration')
const supportsChallengeConsole = computed(() => {
  const mode = props.competition?.gameModeType.toLowerCase()
  return mode === 'ctf' || mode === 'awdp'
})
const hasDedicatedWorkspace = computed(() => isAwd.value || isKoh.value || isPenetration.value)

function signalTone(status: string): 'info' | 'success' | 'warning' | 'danger' {
  const normalized = status.toLowerCase()
  if (normalized === 'running' || normalized === 'active')
    return 'success'
  if (normalized === 'draft' || normalized === 'upcoming' || normalized === 'pending')
    return 'warning'
  if (normalized === 'finished' || normalized === 'ended' || normalized === 'cancelled')
    return 'danger'
  return 'info'
}

function formatDate(value: string | null | undefined) {
  if (!value)
    return 'Not scheduled'

  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return 'Not scheduled'

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}
</script>

<template>
  <section v-if="props.state === 'loading'" class="competition-detail-state">
    <CommandPanel class="competition-detail-state__skeleton" aria-busy="true">
      <span />
      <span />
      <span />
      <span />
    </CommandPanel>
  </section>

  <section v-else-if="props.state === 'invalid' || props.state === 'error'" class="competition-detail-state">
    <CommandPanel class="competition-detail-state__message" tone="warning">
      <div>
        <CommandSignal
          :label="props.state === 'invalid' ? 'Invalid competition identifier' : 'Competition service unavailable'"
          :tone="props.state === 'invalid' ? 'warning' : 'danger'"
        />
        <h1>{{ props.state === 'invalid' ? 'Unable to address this competition' : 'Unable to load competition' }}</h1>
        <p>
          {{
            props.state === 'invalid'
              ? 'The competition route does not contain a valid platform identifier.'
              : (props.errorMessage || 'The service did not return a usable competition record.')
          }}
        </p>
      </div>
      <div class="competition-detail-state__actions">
        <CommandButton label="Back to registry" tone="outline" @click="$emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="props.state === 'error'" label="Retry" @click="$emit('retry')">
          <template #icon><RefreshCw class="size-4" /></template>
        </CommandButton>
      </div>
    </CommandPanel>
  </section>

  <section v-else-if="props.competition" class="competition-detail">
    <header class="competition-detail__heading">
      <div class="competition-detail__title">
        <CommandSignal :label="`${props.competition.gameModeType} / ${props.competition.status}`" :tone="signalTone(props.competition.status)" />
        <h1>{{ props.competition.title }}</h1>
        <p>{{ props.competition.description || 'No competition brief has been published.' }}</p>
      </div>
      <div class="competition-detail__actions">
        <CommandButton label="Back" tone="ghost" @click="$emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <CommandButton label="Register" tone="outline" @click="$emit('register')">
          <template #icon><UserPlus class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="isAwdp" label="Open live screen" @click="$emit('open-awdp-screen')">
          <template #icon><MonitorPlay class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="isAwd" label="Open AWD dashboard" @click="$emit('open-awd-dashboard')">
          <template #icon><Radar class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="isKoh" label="Open KoH dashboard" @click="$emit('open-koh-dashboard')">
          <template #icon><Trophy class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="isPenetration" label="Open penetration range" @click="$emit('open-penetration-dashboard')">
          <template #icon><Crosshair class="size-4" /></template>
        </CommandButton>
      </div>
    </header>

    <CommandPanel class="competition-detail__facts">
      <div>
        <span><CalendarDays class="size-3.5" /> Competition window</span>
        <strong>{{ formatDate(props.competition.startTime) }} - {{ formatDate(props.competition.endTime) }}</strong>
      </div>
      <div>
        <span><UsersRound class="size-3.5" /> Team capacity</span>
        <strong>{{ props.competition.maxTeamMembers ?? 'Not set' }}</strong>
      </div>
      <div>
        <span><Network class="size-3.5" /> Tracks</span>
        <strong>{{ props.competition.tracksEnabled ? (props.competition.trackNames.join(', ') || 'Enabled') : 'Disabled' }}</strong>
      </div>
      <div>
        <span><Gauge class="size-3.5" /> Mode</span>
        <strong>{{ props.competition.gameModeType.toUpperCase() }}</strong>
      </div>
    </CommandPanel>

    <div class="competition-detail__content">
      <CommandChallengeGrid
        v-if="supportsChallengeConsole"
        :challenges="props.challenges"
        :loading="props.loadingChallenges"
        :error="props.challengeError"
        :active-mode="props.competition.gameModeType"
        @select="$emit('select-challenge', $event)"
      />

      <CommandPanel v-else-if="!hasDedicatedWorkspace" class="competition-detail__mode-notice" tone="warning">
        <CommandSignal label="Mode-specific workspace" tone="warning" />
        <h2>{{ props.competition.gameModeType.toUpperCase() }} controls are not migrated yet</h2>
        <p>The competition record and live scoreboard remain available here. Its mode-specific operational workspace will arrive as a dedicated V2 page.</p>
      </CommandPanel>

      <CommandLeaderboardTable
        :entries="props.leaderboard"
        :loading="props.loadingLeaderboard"
        :error="props.leaderboardError"
      />
    </div>
  </section>
</template>

<style scoped>
.competition-detail,
.competition-detail-state {
  display: grid;
  gap: 16px;
}

.competition-detail__heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
  padding: 4px 2px 0;
}

.competition-detail__title h1 {
  margin: 8px 0 0;
  color: var(--v2-text);
  font-size: 24px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.competition-detail__title p {
  max-width: 820px;
  margin: 8px 0 0;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.6;
}

.competition-detail__actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.competition-detail__facts {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  padding: 10px;
}

.competition-detail__facts > div {
  display: grid;
  min-width: 0;
  gap: 7px;
  border-radius: 12px;
  padding: 13px 15px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}

.competition-detail__facts span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--v2-text-faint);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.05em;
}

.competition-detail__facts strong {
  overflow: hidden;
  color: var(--v2-text);
  font-family: var(--v2-font-mono);
  font-size: 12px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.competition-detail__content {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(330px, 0.8fr);
  align-items: start;
  gap: 16px;
}

.competition-detail__mode-notice {
  display: grid;
  min-height: 226px;
  align-content: center;
  gap: 10px;
  padding: 24px;
}

.competition-detail__mode-notice h2 {
  margin: 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
}

.competition-detail__mode-notice p {
  max-width: 570px;
  margin: 0;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.6;
}

.competition-detail-state__skeleton {
  display: grid;
  min-height: 260px;
  grid-template-rows: 14px 30px 1fr 56px;
  gap: 14px;
  padding: 22px;
}

.competition-detail-state__skeleton span {
  display: block;
  border-radius: 12px;
  background: var(--v2-surface-strong);
  box-shadow: var(--v2-inset);
  animation: detail-pulse 1.1s ease-in-out infinite alternate;
}

.competition-detail-state__skeleton span:nth-child(1) { width: 21%; }
.competition-detail-state__skeleton span:nth-child(2) { width: 42%; }
.competition-detail-state__skeleton span:nth-child(4) { width: 76%; }

.competition-detail-state__message {
  display: flex;
  min-height: 210px;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  padding: 26px;
}

.competition-detail-state__message h1 {
  margin: 8px 0 0;
  color: var(--v2-text);
  font-size: 19px;
  font-weight: 600;
}

.competition-detail-state__message p {
  max-width: 650px;
  margin: 8px 0 0;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.6;
}

.competition-detail-state__actions {
  display: flex;
  flex: none;
  gap: 8px;
}

@keyframes detail-pulse {
  to { opacity: 0.45; }
}

@media (max-width: 1080px) {
  .competition-detail__facts { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .competition-detail__content { grid-template-columns: 1fr; }
}

@media (max-width: 700px) {
  .competition-detail__heading,
  .competition-detail-state__message {
    align-items: flex-start;
    flex-direction: column;
  }

  .competition-detail__actions,
  .competition-detail-state__actions {
    justify-content: flex-start;
  }
}

@media (max-width: 480px) {
  .competition-detail__facts { grid-template-columns: 1fr; }
}
</style>
