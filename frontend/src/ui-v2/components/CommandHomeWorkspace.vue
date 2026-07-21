<script setup lang="ts">
import { ArrowRight, ListChecks, Trophy, UsersRound } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandCompetitionCard, { type CommandCompetition } from './CommandCompetitionCard.vue'

export interface CommandHomeTeam {
  id: string
  competitionId: string
  name: string
  competitionTitle: string
  registrationStatus: string
  gameModeType: string
  memberCount: number | null
  maxTeamMembers: number | null
}

export interface CommandHomeTeamStats {
  joined: number
  ready: number
  pending: number
}

const props = defineProps<{
  operatorName: string
  competitions: CommandCompetition[]
  loadingCompetitions: boolean
  teams: CommandHomeTeam[]
  loadingTeams: boolean
  teamStats: CommandHomeTeamStats
}>()

const emit = defineEmits<{
  browseCompetitions: []
  manageTeams: []
  openCompetition: [id: string]
  registerCompetition: [id: string]
  enterCompetition: [competitionId: string]
}>()

const onboardingSteps = [
  {
    icon: ListChecks,
    title: 'Register per competition',
    text: 'Each competition owns its team roster, tracks, and approval rules.',
  },
  {
    icon: UsersRound,
    title: 'Manage teams separately',
    text: 'Use the team page for tokens, locks, membership, and status checks.',
  },
  {
    icon: Trophy,
    title: 'Enter when ready',
    text: 'Approved teams unlock challenges, submissions, patches, and scoreboards.',
  },
]

function statusTone(status: string): 'info' | 'success' | 'warning' | 'danger' {
  const value = status.toLowerCase()
  if (value === 'approved' || value === 'running' || value === 'active')
    return 'success'
  if (value === 'pending' || value === 'draft' || value === 'upcoming')
    return 'warning'
  if (value === 'rejected' || value === 'banned' || value === 'finished')
    return 'danger'
  return 'info'
}

function canEnter(team: CommandHomeTeam) {
  return team.registrationStatus.toLowerCase() === 'approved'
}
</script>

<template>
  <section class="home-workspace">
    <section class="home-workspace__hero">
      <CommandPanel class="home-hero" tone="signal">
        <CommandSignal label="NoCTF / Competition control room" tone="success" />
        <h1>Welcome, {{ props.operatorName }}</h1>
        <p>
          Start from one place: find a competition, manage your teams, check
          registration state, or jump back into an active event.
        </p>
        <div class="home-hero__actions">
          <CommandButton label="Browse competitions" @click="emit('browseCompetitions')">
            <template #icon><Trophy class="size-4" /></template>
          </CommandButton>
          <CommandButton label="Manage teams" tone="ghost" @click="emit('manageTeams')">
            <template #icon><UsersRound class="size-4" /></template>
          </CommandButton>
        </div>
      </CommandPanel>

      <CommandPanel class="home-state">
        <header class="home-state__header">
          <div>
            <CommandSignal label="Enrollment telemetry" tone="info" />
            <h2>Your competition state</h2>
          </div>
          <UsersRound class="size-4 text-[var(--v2-info)]" />
        </header>
        <dl class="home-state__grid">
          <div>
            <dt>Joined teams</dt>
            <dd>{{ String(props.teamStats.joined).padStart(2, '0') }}</dd>
          </div>
          <div>
            <dt>Ready teams</dt>
            <dd class="home-state__ready">{{ String(props.teamStats.ready).padStart(2, '0') }}</dd>
          </div>
          <div>
            <dt>Pending review</dt>
            <dd :class="{ 'home-state__pending': props.teamStats.pending > 0 }">
              {{ String(props.teamStats.pending).padStart(2, '0') }}
            </dd>
          </div>
        </dl>
      </CommandPanel>
    </section>

    <section class="home-section">
      <header class="home-section__heading">
        <div>
          <CommandSignal label="Live registry" tone="success" />
          <h2>Competitions to watch</h2>
        </div>
        <CommandButton label="View all" tone="ghost" @click="emit('browseCompetitions')">
          <template #icon><ArrowRight class="size-4" /></template>
        </CommandButton>
      </header>

      <CommandPanel v-if="props.loadingCompetitions" class="home-section__state" aria-busy="true">
        <CommandSignal label="Synchronizing competitions" tone="info" />
        <p>Loading the competition registry.</p>
      </CommandPanel>
      <CommandPanel v-else-if="!props.competitions.length" class="home-section__state">
        <CommandSignal label="Registry empty" tone="info" />
        <p>No competitions are published yet. Check back after the next event is announced.</p>
      </CommandPanel>
      <div v-else class="home-section__competitions">
        <CommandCompetitionCard
          v-for="competition in props.competitions"
          :key="competition.id"
          :competition="competition"
          @open="emit('openCompetition', $event)"
          @register="emit('registerCompetition', $event)"
        />
      </div>
    </section>

    <section class="home-section">
      <header class="home-section__heading">
        <div>
          <CommandSignal label="Account enrollment" tone="info" />
          <h2>Recent teams</h2>
        </div>
        <CommandButton label="Manage teams" tone="ghost" @click="emit('manageTeams')">
          <template #icon><ArrowRight class="size-4" /></template>
        </CommandButton>
      </header>

      <CommandPanel v-if="props.loadingTeams" class="home-section__state" aria-busy="true">
        <CommandSignal label="Synchronizing teams" tone="info" />
        <p>Loading your team records.</p>
      </CommandPanel>
      <CommandPanel v-else-if="!props.teams.length" class="home-section__state">
        <CommandSignal label="No team records" tone="info" />
        <p>You have not joined a team yet. Create or join one from the teams page.</p>
      </CommandPanel>
      <div v-else class="home-section__teams">
        <CommandPanel v-for="team in props.teams" :key="team.id" class="home-team">
          <div class="home-team__body">
            <CommandSignal :label="team.registrationStatus" :tone="statusTone(team.registrationStatus)" />
            <h3>{{ team.name }}</h3>
            <p>{{ team.competitionTitle }} / {{ team.gameModeType.toUpperCase() }}</p>
          </div>
          <div class="home-team__meta">
            <span class="home-team__members">{{ team.memberCount ?? 0 }}/{{ team.maxTeamMembers ?? '-' }} members</span>
            <CommandButton
              v-if="canEnter(team)"
              label="Enter"
              @click="emit('enterCompetition', team.competitionId)"
            >
              <template #icon><ArrowRight class="size-4" /></template>
            </CommandButton>
          </div>
        </CommandPanel>
      </div>
    </section>

    <section class="home-steps">
      <CommandPanel v-for="step in onboardingSteps" :key="step.title" class="home-step">
        <span class="home-step__icon"><component :is="step.icon" class="size-4" /></span>
        <h3>{{ step.title }}</h3>
        <p>{{ step.text }}</p>
      </CommandPanel>
    </section>
  </section>
</template>

<style scoped>
.home-workspace { display: grid; gap: 22px; }

.home-workspace__hero { display: grid; grid-template-columns: minmax(0, 1fr) minmax(320px, 0.42fr); gap: 18px; align-items: stretch; }

.home-hero { display: grid; align-content: start; gap: 14px; padding: 24px 24px 22px; }
.home-hero h1 { margin: 4px 0 0; color: var(--v2-text); font-size: 28px; font-weight: 600; letter-spacing: -0.01em; }
.home-hero p { max-width: 62ch; margin: 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.7; }
.home-hero__actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 8px; }

.home-state { display: grid; align-content: start; gap: 16px; padding: 20px; }
.home-state__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; }
.home-state__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 16px; font-weight: 600; }
.home-state__grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; margin: 0; }
.home-state__grid > div { display: grid; gap: 8px; border-radius: 12px; padding: 14px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.home-state__grid dt { color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.04em; }
.home-state__grid dd { margin: 0; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 22px; font-weight: 600; line-height: 1; }
.home-state__ready { color: var(--v2-cyan); }
.home-state__pending { color: var(--v2-warning); }

.home-section { display: grid; gap: 12px; }
.home-section__heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 14px; padding: 0 2px; }
.home-section__heading h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.home-section__state { display: grid; min-height: 120px; align-content: center; justify-items: start; gap: 8px; padding: 22px; }
.home-section__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.home-section__competitions { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }

.home-section__teams { display: grid; gap: 12px; }
.home-team { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 14px 18px; }
.home-team__body { display: grid; min-width: 0; gap: 6px; }
.home-team__body h3 { overflow: hidden; margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.home-team__body p { overflow: hidden; margin: 0; color: var(--v2-text-muted); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.home-team__meta { display: flex; flex: none; align-items: center; gap: 14px; }
.home-team__members { color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; font-weight: 600; }

.home-steps { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }
.home-step { display: grid; align-content: start; gap: 10px; padding: 20px; }
.home-step__icon { display: grid; width: 38px; height: 38px; place-items: center; border-radius: 999px; background: var(--v2-surface); box-shadow: var(--v2-inset); color: var(--v2-primary); }
.home-step h3 { margin: 2px 0 0; color: var(--v2-text); font-size: 14px; font-weight: 600; }
.home-step p { margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.6; }

@media (max-width: 1120px) {
  .home-workspace__hero { grid-template-columns: 1fr; }
  .home-section__competitions { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 760px) {
  .home-section__competitions, .home-steps { grid-template-columns: 1fr; }
  .home-state__grid { grid-template-columns: 1fr; }
  .home-team { align-items: flex-start; flex-direction: column; }
  .home-team__meta { width: 100%; justify-content: space-between; }
}
</style>
