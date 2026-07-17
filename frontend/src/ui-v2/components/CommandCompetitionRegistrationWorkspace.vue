<script setup lang="ts">
import { ArrowLeft, ArrowRight, CalendarDays, CheckCircle2, Clipboard, KeyRound, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect, { type CommandSelectOption } from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandRegistrationCompetition {
  id: string
  title: string
  description: string | null
  status: string
  gameModeType: string
  startTime: string | null
  maxTeamMembers: number | null
  teamRegistrationAutoApprove: boolean
  tracksEnabled: boolean
  trackNames: string[]
}

export interface CommandRegistrationTeam {
  id: string
  competitionId: string
  name: string
  inviteToken: string | null
  memberCount: number | null
  isLocked: boolean
  isBanned: boolean
  bannedReason: string | null
  trackName: string | null
  registrationStatus: string
  isCaptain: boolean
}

type RegistrationState = 'invalid' | 'loading' | 'error' | 'ready'

const props = defineProps<{
  state: RegistrationState
  errorMessage?: string
  competition?: CommandRegistrationCompetition
  teams: CommandRegistrationTeam[]
  loadingTeams: boolean
  teamsError: boolean
  newTeamName: string
  selectedTrackName: string
  joinToken: string
  createPending: boolean
  joinPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

defineEmits<{
  back: []
  retry: []
  enter: []
  create: []
  join: []
  copy: [token: string]
  'update:newTeamName': [value: string]
  'update:selectedTrackName': [value: string]
  'update:joinToken': [value: string]
}>()

const currentTeam = computed(() => {
  const approvedTeams = props.teams.filter(team => team.registrationStatus.toLowerCase() === 'approved')
  return approvedTeams.find(team => !team.isBanned) ?? approvedTeams[0] ?? props.teams[0] ?? null
})
const canEnter = computed(() => Boolean(
  currentTeam.value?.registrationStatus.toLowerCase() === 'approved'
  && !currentTeam.value.isBanned,
))
const canCreate = computed(() => Boolean(
  props.newTeamName.trim()
  && (!props.competition?.tracksEnabled || props.selectedTrackName),
))
const trackOptions = computed<CommandSelectOption[]>(() => [
  { value: '', label: 'Select track' },
  ...(props.competition?.trackNames ?? []).map(track => ({ value: track, label: track })),
])

function signalTone(status: string): 'info' | 'success' | 'warning' | 'danger' {
  const value = status.toLowerCase()
  if (value === 'approved' || value === 'running' || value === 'active')
    return 'success'
  if (value === 'pending' || value === 'draft' || value === 'upcoming')
    return 'warning'
  if (value === 'rejected' || value === 'banned' || value === 'finished')
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
  <section v-if="props.state === 'loading'" class="registration-state">
    <CommandPanel class="registration-state__skeleton" aria-busy="true">
      <span />
      <span />
      <span />
      <span />
    </CommandPanel>
  </section>

  <section v-else-if="props.state === 'invalid' || props.state === 'error'" class="registration-state">
    <CommandPanel class="registration-state__message" tone="warning">
      <div>
        <CommandSignal
          :label="props.state === 'invalid' ? 'Invalid competition identifier' : 'Registration service unavailable'"
          :tone="props.state === 'invalid' ? 'warning' : 'danger'"
        />
        <h1>{{ props.state === 'invalid' ? 'Unable to address this registration' : 'Unable to load registration' }}</h1>
        <p>
          {{
            props.state === 'invalid'
              ? 'The registration route does not contain a valid platform identifier.'
              : (props.errorMessage || 'The service did not return a usable competition record.')
          }}
        </p>
      </div>
      <div class="registration-state__actions">
        <CommandButton label="Back to registry" tone="outline" @click="$emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="props.state === 'error'" label="Retry" @click="$emit('retry')" />
      </div>
    </CommandPanel>
  </section>

  <section v-else-if="props.competition" class="registration-workspace">
    <header class="registration-workspace__heading">
      <div>
        <CommandSignal :label="`${props.competition.gameModeType} / team registration`" :tone="signalTone(props.competition.status)" />
        <h1>{{ props.competition.title }}</h1>
        <p>{{ props.competition.description || 'Create a competition team or join one with an invitation token.' }}</p>
      </div>
      <div class="registration-workspace__actions">
        <CommandButton label="Back" tone="ghost" @click="$emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="canEnter" label="Enter competition" @click="$emit('enter')">
          <template #icon><ArrowRight class="size-4" /></template>
        </CommandButton>
      </div>
    </header>

    <CommandPanel class="registration-workspace__facts">
      <div>
        <span><CalendarDays class="size-3.5" /> Starts</span>
        <strong>{{ formatDate(props.competition.startTime) }}</strong>
      </div>
      <div>
        <span><UsersRound class="size-3.5" /> Team capacity</span>
        <strong>{{ props.competition.maxTeamMembers ?? 'Not set' }}</strong>
      </div>
      <div>
        <span><CheckCircle2 class="size-3.5" /> Registration</span>
        <strong>{{ props.competition.teamRegistrationAutoApprove ? 'Auto approved' : 'Manual review' }}</strong>
      </div>
      <div>
        <span><KeyRound class="size-3.5" /> Tracks</span>
        <strong>{{ props.competition.tracksEnabled ? (props.competition.trackNames.join(', ') || 'Required') : 'Not used' }}</strong>
      </div>
    </CommandPanel>

    <p
      v-if="props.operationMessage"
      class="registration-workspace__message"
      :class="`registration-workspace__message--${props.operationTone || 'success'}`"
      role="status"
    >
      {{ props.operationMessage }}
    </p>

    <section class="registration-workspace__content">
      <CommandPanel class="registration-workspace__primary">
        <header>
          <div>
            <CommandSignal label="Team enrollment" tone="info" />
            <h2>Registration station</h2>
          </div>
          <UsersRound class="size-4 text-[var(--v2-primary)]" />
        </header>

        <div v-if="props.loadingTeams" class="registration-workspace__loading">Synchronizing team enrollment...</div>

        <div v-else-if="props.teamsError" class="registration-workspace__loading registration-workspace__loading--error">
          <span>Team enrollment could not be synchronized. Retry before changing registration.</span>
          <CommandButton label="Retry team sync" tone="outline" @click="$emit('retry')" />
        </div>

        <div v-else-if="currentTeam" class="registration-team">
          <div class="registration-team__topline">
            <div>
              <CommandSignal :label="currentTeam.registrationStatus" :tone="signalTone(currentTeam.registrationStatus)" />
              <h3>{{ currentTeam.name }}</h3>
            </div>
            <span v-if="currentTeam.trackName">{{ currentTeam.trackName }}</span>
          </div>

          <dl class="registration-team__facts">
            <div><dt>Members</dt><dd>{{ currentTeam.memberCount ?? 0 }} / {{ props.competition.maxTeamMembers ?? '-' }}</dd></div>
            <div><dt>Access</dt><dd>{{ currentTeam.isLocked ? 'Locked' : 'Open' }}</dd></div>
            <div><dt>Role</dt><dd>{{ currentTeam.isCaptain ? 'Captain' : 'Member' }}</dd></div>
          </dl>

          <div v-if="currentTeam.inviteToken" class="registration-team__token">
            <code>{{ currentTeam.inviteToken }}</code>
            <CommandIconButton :icon="Clipboard" label="Copy invite token" compact @click="$emit('copy', currentTeam.inviteToken)" />
          </div>

          <p v-if="currentTeam.isBanned" class="registration-team__alert registration-team__alert--danger">
            {{ currentTeam.bannedReason || 'This team is restricted from participant actions.' }}
          </p>
          <p v-else-if="currentTeam.registrationStatus.toLowerCase() === 'rejected'" class="registration-team__alert registration-team__alert--danger">
            Team registration was rejected.
          </p>
          <p v-else-if="currentTeam.registrationStatus.toLowerCase() !== 'approved'" class="registration-team__alert">
            Team registration is awaiting approval.
          </p>
        </div>

        <div v-else class="registration-forms">
          <form class="registration-form" @submit.prevent="$emit('create')">
            <div>
              <CommandSignal label="New team" tone="success" />
              <h3>Create a team</h3>
            </div>
            <CommandInput
              :model-value="props.newTeamName"
              label="Team name"
              type="text"
              placeholder="Team name"
              @update:model-value="$emit('update:newTeamName', $event)"
            />
            <CommandSelect
              v-if="props.competition.tracksEnabled"
              :model-value="props.selectedTrackName"
              label="Competition track"
              :options="trackOptions"
              @update:model-value="$emit('update:selectedTrackName', $event)"
            />
            <CommandButton
              :label="props.createPending ? 'Creating team' : 'Create team'"
              :disabled="!canCreate || props.createPending"
              @click="$emit('create')"
            />
          </form>

          <form class="registration-form" @submit.prevent="$emit('join')">
            <div>
              <CommandSignal label="Existing team" tone="warning" />
              <h3>Join by token</h3>
            </div>
            <CommandInput
              :model-value="props.joinToken"
              label="Invitation token"
              type="text"
              placeholder="Invitation token"
              @update:model-value="$emit('update:joinToken', $event)"
            />
            <CommandButton
              label="Join team"
              tone="outline"
              :disabled="!props.joinToken.trim() || props.joinPending"
              @click="$emit('join')"
            />
          </form>
        </div>
      </CommandPanel>

      <CommandPanel class="registration-workspace__policy" tone="warning">
        <CommandSignal label="Registration policy" tone="warning" />
        <h2>Participant status</h2>
        <dl>
          <div><dt>One team</dt><dd>Each member can use one team in this competition.</dd></div>
          <div><dt>Approval</dt><dd>{{ props.competition.teamRegistrationAutoApprove ? 'New teams are approved automatically.' : 'New teams require organizer review.' }}</dd></div>
          <div><dt>Track</dt><dd>{{ props.competition.tracksEnabled ? 'Choose a competition track when creating a team.' : 'This competition does not use tracks.' }}</dd></div>
        </dl>
      </CommandPanel>
    </section>
  </section>
</template>

<style scoped>
.registration-workspace,
.registration-state { display: grid; gap: 16px; }
.registration-workspace__heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; padding: 4px 2px 0; }
.registration-workspace__heading h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 600; }
.registration-workspace__heading p { max-width: 780px; margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }
.registration-workspace__actions { display: flex; flex: none; flex-wrap: wrap; justify-content: flex-end; gap: 8px; }
.registration-workspace__facts { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; padding: 10px; }
.registration-workspace__facts > div { display: grid; min-width: 0; gap: 7px; border-radius: 12px; padding: 13px 15px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.registration-workspace__facts span { display: inline-flex; align-items: center; gap: 6px; color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.05em; }
.registration-workspace__facts strong { overflow: hidden; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 12px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.registration-workspace__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.registration-workspace__message--danger { color: var(--v2-danger); }
.registration-workspace__content { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(300px, 0.8fr); align-items: start; gap: 16px; }
.registration-workspace__primary { display: grid; }
.registration-workspace__primary > header { display: flex; min-height: 76px; align-items: center; justify-content: space-between; padding: 16px 18px 10px; }
.registration-workspace__primary h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.registration-workspace__loading { display: grid; min-height: 220px; place-content: center; gap: 12px; padding: 28px 18px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 12px; text-align: center; }
.registration-workspace__loading--error { color: var(--v2-danger); }
.registration-team { display: grid; gap: 16px; padding: 8px 18px 18px; }
.registration-team__topline { display: flex; align-items: flex-start; justify-content: space-between; gap: 14px; }
.registration-team__topline h3, .registration-form h3 { margin: 8px 0 0; color: var(--v2-text); font-size: 16px; font-weight: 600; }
.registration-team__topline > span { border-radius: 999px; padding: 4px 10px; background: var(--v2-surface); box-shadow: var(--v2-raised-sm); color: var(--v2-primary); font-family: var(--v2-font-mono); font-size: 10px; font-weight: 600; letter-spacing: 0.03em; }
.registration-team__facts { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; margin: 0; }
.registration-team__facts div { display: grid; gap: 6px; border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.registration-team__facts dt { color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.05em; }
.registration-team__facts dd { margin: 0; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 12px; font-weight: 600; }
.registration-team__token { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 8px; border-radius: 12px; padding: 6px 6px 6px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.registration-team__token code { overflow: hidden; color: var(--v2-text-muted); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.registration-team__alert { margin: 0; border-radius: 12px; padding: 11px 13px; color: var(--v2-warning); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; line-height: 1.5; }
.registration-team__alert--danger { color: var(--v2-danger); }
.registration-forms { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; padding: 8px 18px 18px; }
.registration-form { display: grid; min-width: 0; align-content: start; gap: 13px; border-radius: 14px; padding: 16px; background: var(--v2-surface); box-shadow: var(--v2-raised-sm); }
.registration-form :deep(.command-button) { width: 100%; }
.registration-workspace__policy { display: grid; align-content: start; gap: 12px; padding: 18px; }
.registration-workspace__policy h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.registration-workspace__policy dl { display: grid; gap: 10px; margin: 0; }
.registration-workspace__policy dl div { display: grid; gap: 6px; border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.registration-workspace__policy dt { color: var(--v2-text); font-size: 13px; font-weight: 600; }
.registration-workspace__policy dd { margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.55; }
.registration-state__skeleton { display: grid; min-height: 260px; grid-template-rows: 14px 30px 1fr 56px; gap: 14px; padding: 22px; }
.registration-state__skeleton span { display: block; border-radius: 12px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); animation: registration-pulse 1.1s ease-in-out infinite alternate; }
.registration-state__skeleton span:nth-child(1) { width: 21%; }
.registration-state__skeleton span:nth-child(2) { width: 42%; }
.registration-state__skeleton span:nth-child(4) { width: 76%; }
.registration-state__message { display: flex; min-height: 210px; align-items: center; justify-content: space-between; gap: 20px; padding: 26px; }
.registration-state__message h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 19px; font-weight: 600; }
.registration-state__message p { max-width: 650px; margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }
.registration-state__actions { display: flex; flex: none; gap: 8px; }
@keyframes registration-pulse { to { opacity: 0.45; } }
@media (max-width: 1080px) {
  .registration-workspace__facts { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .registration-workspace__content { grid-template-columns: 1fr; }
}
@media (max-width: 700px) {
  .registration-workspace__heading, .registration-state__message { align-items: flex-start; flex-direction: column; }
  .registration-workspace__actions, .registration-state__actions { justify-content: flex-start; }
  .registration-forms { grid-template-columns: 1fr; }
}
@media (max-width: 480px) {
  .registration-workspace__facts, .registration-team__facts { grid-template-columns: 1fr; }
}
</style>
