<script setup lang="ts">
import { ArrowRight, Clipboard, KeyRound, Lock, LogOut, Plus, ShieldAlert, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect, { type CommandSelectOption } from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandMyTeam {
  id: string
  competitionId: string
  name: string
  inviteToken: string | null
  registrationStatus: string
  competitionTitle: string
  competitionStatus: string
  gameModeType: string
  memberCount: number | null
  maxTeamMembers: number | null
  isCaptain: boolean
  isLocked: boolean
  isBanned: boolean
  trackName: string | null
}

export interface CommandTeamCompetition {
  id: string
  title: string
  status: string
  gameModeType: string
}

type TeamsState = 'loading' | 'error' | 'ready'

const props = defineProps<{
  state: TeamsState
  errorMessage?: string
  teams: CommandMyTeam[]
  competitions: CommandTeamCompetition[]
  loadingCompetitions: boolean
  selectedCompetitionId: string
  selectedTrackName: string
  tracks: string[]
  tracksRequired: boolean
  loadingTracks: boolean
  newTeamName: string
  joinToken: string
  createPending: boolean
  joinPending: boolean
  leavingTeamId: string | null
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

defineEmits<{
  retry: []
  create: []
  join: []
  leave: [team: CommandMyTeam]
  copy: [token: string]
  open: [competitionId: string]
  'update:selectedCompetitionId': [value: string]
  'update:selectedTrackName': [value: string]
  'update:newTeamName': [value: string]
  'update:joinToken': [value: string]
}>()

const activeTeams = computed(() => props.teams.filter(team => !team.isBanned))
const bannedTeams = computed(() => props.teams.filter(team => team.isBanned))
const competitionOptions = computed<CommandSelectOption[]>(() => [
  { value: '', label: props.loadingCompetitions ? 'Synchronizing competitions...' : 'Select competition' },
  ...props.competitions.map(competition => ({
    value: competition.id,
    label: `${competition.title} / ${competition.gameModeType.toUpperCase()}`,
  })),
])
const trackOptions = computed<CommandSelectOption[]>(() => [
  { value: '', label: props.loadingTracks ? 'Synchronizing tracks...' : 'Select track' },
  ...props.tracks.map(track => ({ value: track, label: track })),
])
const canCreate = computed(() => Boolean(
  props.selectedCompetitionId
  && props.newTeamName.trim()
  && (!props.tracksRequired || props.selectedTrackName),
))

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

function canEnter(team: CommandMyTeam) {
  return team.registrationStatus.toLowerCase() === 'approved' && !team.isBanned
}
</script>

<template>
  <section class="teams-workspace">
    <header class="teams-workspace__heading">
      <div>
        <CommandSignal label="Team service / member operations" tone="success" />
        <h1>My teams</h1>
        <p>Create, join, and enter the teams associated with your account.</p>
      </div>
      <div class="teams-workspace__count">
        <UsersRound class="size-4" />
        <strong>{{ String(props.teams.length).padStart(2, '0') }}</strong>
        <span>records</span>
      </div>
    </header>

    <p
      v-if="props.operationMessage"
      class="teams-workspace__message"
      :class="`teams-workspace__message--${props.operationTone || 'success'}`"
      role="status"
    >
      {{ props.operationMessage }}
    </p>

    <section class="teams-workspace__commands">
      <CommandPanel class="teams-workspace__command">
        <header>
          <div>
            <CommandSignal label="New team" tone="success" />
            <h2>Create a team</h2>
          </div>
          <Plus class="size-4 text-[var(--v2-cyan)]" />
        </header>
        <CommandSelect
          :model-value="props.selectedCompetitionId"
          label="Competition for new team"
          :options="competitionOptions"
          @update:model-value="$emit('update:selectedCompetitionId', $event)"
        />
        <CommandInput
          :model-value="props.newTeamName"
          label="New team name"
          type="text"
          placeholder="Team name"
          @update:model-value="$emit('update:newTeamName', $event)"
        />
        <CommandSelect
          v-if="props.tracksRequired"
          :model-value="props.selectedTrackName"
          label="Competition track"
          :options="trackOptions"
          @update:model-value="$emit('update:selectedTrackName', $event)"
        />
        <CommandButton :label="props.createPending ? 'Creating team' : 'Create team'" :disabled="!canCreate || props.createPending" @click="$emit('create')" />
      </CommandPanel>

      <CommandPanel class="teams-workspace__command" tone="warning">
        <header>
          <div>
            <CommandSignal label="Existing team" tone="warning" />
            <h2>Join by token</h2>
          </div>
          <KeyRound class="size-4 text-[var(--v2-warning)]" />
        </header>
        <CommandInput
          :model-value="props.joinToken"
          label="Invitation token"
          type="text"
          placeholder="Invitation token"
          @update:model-value="$emit('update:joinToken', $event)"
        />
        <CommandButton label="Join team" tone="outline" :disabled="!props.joinToken.trim() || props.joinPending" @click="$emit('join')" />
      </CommandPanel>
    </section>

    <CommandPanel v-if="props.state === 'loading'" class="teams-workspace__state" aria-busy="true">
      <CommandSignal label="Synchronizing teams" tone="info" />
      <h2>Loading team records</h2>
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'error'" class="teams-workspace__state" tone="warning">
      <CommandSignal label="Team service unavailable" tone="danger" />
      <h2>Unable to load teams</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable team list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="$emit('retry')" />
    </CommandPanel>

    <CommandPanel v-else-if="!props.teams.length" class="teams-workspace__state">
      <CommandSignal label="No team records" tone="info" />
      <h2>No teams associated with this account</h2>
      <p>Use the enrollment commands above to create a team or join one with an invitation token.</p>
    </CommandPanel>

    <template v-else>
      <section class="teams-workspace__section">
        <header class="teams-workspace__section-heading">
          <div>
            <CommandSignal label="Active enrollment" tone="success" />
            <h2>Active teams</h2>
          </div>
          <span>{{ String(activeTeams.length).padStart(2, '0') }}</span>
        </header>

        <div v-if="activeTeams.length" class="teams-workspace__grid">
          <CommandPanel v-for="team in activeTeams" :key="team.id" class="team-card" :tone="canEnter(team) ? 'signal' : 'default'">
            <header>
              <div>
                <CommandSignal :label="team.registrationStatus" :tone="statusTone(team.registrationStatus)" />
                <h3>{{ team.name }}</h3>
                <p>{{ team.competitionTitle }}</p>
              </div>
              <span>{{ team.gameModeType.toUpperCase() }}</span>
            </header>
            <dl>
              <div><dt>Members</dt><dd>{{ team.memberCount ?? 0 }} / {{ team.maxTeamMembers ?? '-' }}</dd></div>
              <div><dt>Role</dt><dd>{{ team.isCaptain ? 'Captain' : 'Member' }}</dd></div>
              <div><dt>Access</dt><dd>{{ team.isLocked ? 'Locked' : 'Open' }}</dd></div>
              <div v-if="team.trackName"><dt>Track</dt><dd>{{ team.trackName }}</dd></div>
            </dl>
            <div v-if="team.inviteToken" class="team-card__token">
              <code>{{ team.inviteToken }}</code>
              <CommandIconButton :icon="Clipboard" label="Copy invite token" compact @click="$emit('copy', team.inviteToken)" />
            </div>
            <p v-if="!canEnter(team)" class="team-card__notice">
              {{ team.registrationStatus.toLowerCase() === 'rejected' ? 'Team registration was rejected.' : 'Team registration is awaiting approval.' }}
            </p>
            <footer>
              <CommandButton v-if="canEnter(team)" label="Enter" @click="$emit('open', team.competitionId)">
                <template #icon><ArrowRight class="size-4" /></template>
              </CommandButton>
              <CommandButton
                v-if="!team.isLocked"
                :label="props.leavingTeamId === team.id ? 'Leaving team' : 'Leave'"
                tone="ghost"
                :disabled="Boolean(props.leavingTeamId)"
                @click="$emit('leave', team)"
              >
                <template #icon><LogOut class="size-4" /></template>
              </CommandButton>
            </footer>
          </CommandPanel>
        </div>
        <CommandPanel v-else class="teams-workspace__state">
          <h2>No active team records</h2>
        </CommandPanel>
      </section>

      <section v-if="bannedTeams.length" class="teams-workspace__section">
        <header class="teams-workspace__section-heading">
          <div>
            <CommandSignal label="Restricted enrollment" tone="danger" />
            <h2>Restricted teams</h2>
          </div>
          <ShieldAlert class="size-4 text-[var(--v2-danger)]" />
        </header>
        <div class="teams-workspace__grid">
          <CommandPanel v-for="team in bannedTeams" :key="team.id" class="team-card" tone="warning">
            <header>
              <div>
                <CommandSignal label="Restricted" tone="danger" />
                <h3>{{ team.name }}</h3>
                <p>{{ team.competitionTitle }}</p>
              </div>
              <Lock class="size-4 text-[var(--v2-danger)]" />
            </header>
            <p class="team-card__notice team-card__notice--danger">This team is restricted from participant actions.</p>
          </CommandPanel>
        </div>
      </section>
    </template>
  </section>
</template>

<style scoped>
.teams-workspace { display: grid; gap: 16px; }
.teams-workspace__heading { display: flex; align-items: end; justify-content: space-between; gap: 20px; }
.teams-workspace__heading h1 { margin: 7px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 680; }
.teams-workspace__heading p { margin: 7px 0 0; color: var(--v2-text-muted); font-size: 12px; }
.teams-workspace__count { display: grid; grid-template-columns: auto auto; align-items: center; column-gap: 8px; border-left: 1px solid var(--v2-line); padding-left: 13px; color: var(--v2-primary); }
.teams-workspace__count strong { color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 22px; line-height: 1; }
.teams-workspace__count span { grid-column: 2; color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
.teams-workspace__message { margin: 0; border: 1px solid var(--v2-cyan); padding: 10px 12px; color: var(--v2-cyan); background: rgb(34 245 199 / 0.07); font-size: 12px; }
.teams-workspace__message--danger { border-color: var(--v2-danger); color: var(--v2-danger); background: rgb(255 84 112 / 0.07); }
.teams-workspace__commands { display: grid; grid-template-columns: minmax(0, 1.15fr) minmax(260px, 0.85fr); gap: 12px; }
.teams-workspace__command { display: grid; align-content: start; gap: 10px; padding: 15px; }
.teams-workspace__command > header { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding-bottom: 5px; }
.teams-workspace__command h2 { margin: 5px 0 0; color: var(--v2-text); font-size: 15px; font-weight: 650; }
.teams-workspace__command :deep(.command-button) { justify-self: start; }
.teams-workspace__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 8px; padding: 22px; }
.teams-workspace__state h2 { margin: 0; color: var(--v2-text); font-size: 16px; font-weight: 650; }
.teams-workspace__state p { max-width: 680px; margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.6; }
.teams-workspace__section { display: grid; gap: 10px; }
.teams-workspace__section-heading { display: flex; align-items: center; justify-content: space-between; }
.teams-workspace__section-heading h2 { margin: 5px 0 0; color: var(--v2-text); font-size: 16px; font-weight: 650; }
.teams-workspace__section-heading > span { color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; font-weight: 700; }
.teams-workspace__grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.team-card { display: grid; min-height: 250px; grid-template-rows: auto auto auto 1fr auto; }
.team-card > header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; border-bottom: 1px solid var(--v2-line); padding: 14px 15px; }
.team-card h3 { overflow: hidden; margin: 6px 0 0; color: var(--v2-text); font-size: 15px; font-weight: 650; text-overflow: ellipsis; white-space: nowrap; }
.team-card header p { overflow: hidden; margin: 5px 0 0; color: var(--v2-text-muted); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.team-card header > span { border: 1px solid var(--v2-line-bright); padding: 4px 5px; color: var(--v2-primary); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; font-weight: 700; letter-spacing: 0.08em; }
.team-card dl { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0; margin: 0; border-bottom: 1px solid var(--v2-line); }
.team-card dl div { display: grid; gap: 4px; border-right: 1px solid var(--v2-line); border-bottom: 1px solid var(--v2-line); padding: 10px 14px; }
.team-card dl div:nth-child(2n) { border-right: 0; }
.team-card dl div:nth-last-child(-n + 2):nth-child(odd) { border-bottom: 0; }
.team-card dl div:last-child { border-bottom: 0; }
.team-card dt { color: var(--v2-text-faint); font-size: 9px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; }
.team-card dd { overflow: hidden; margin: 0; color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; font-weight: 650; text-overflow: ellipsis; white-space: nowrap; }
.team-card__token { display: grid; grid-template-columns: minmax(0, 1fr) auto; margin: 12px 14px 0; border: 1px solid var(--v2-line); }
.team-card__token code { overflow: hidden; padding: 9px 10px; color: var(--v2-text-muted); font-size: 10px; text-overflow: ellipsis; white-space: nowrap; }
.team-card__token :deep(.command-icon-button) { border-left: 1px solid var(--v2-line); }
.team-card__notice { margin: 12px 14px 0; border-left: 2px solid var(--v2-warning); padding: 8px 9px; color: var(--v2-text-muted); background: rgb(255 209 102 / 0.05); font-size: 11px; line-height: 1.45; }
.team-card__notice--danger { border-color: var(--v2-danger); color: var(--v2-danger); background: rgb(255 84 112 / 0.06); }
.team-card > footer { display: flex; justify-content: flex-end; gap: 7px; border-top: 1px solid var(--v2-line); margin-top: 14px; padding: 10px 12px; }
@media (max-width: 1120px) { .teams-workspace__grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 760px) {
  .teams-workspace__heading { align-items: flex-start; flex-direction: column; }
  .teams-workspace__count { border-left: 0; padding-left: 0; }
  .teams-workspace__commands, .teams-workspace__grid { grid-template-columns: 1fr; }
}
</style>
