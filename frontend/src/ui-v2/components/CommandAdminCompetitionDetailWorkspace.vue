<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  ArrowLeft,
  Check,
  Edit3,
  Lock,
  Plus,
  RefreshCw,
  RotateCw,
  Save,
  Settings2,
  ShieldAlert,
  Trash2,
  Unlock,
  X,
} from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandCheckbox from '../primitives/CommandCheckbox.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandTextarea from '../primitives/CommandTextarea.vue'
import { normalizeDirection } from '@/lib/challengeDirections'

export interface CommandAdminCompetitionForm {
  title: string
  description: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
  initialPoints: string
  minimumPoints: string
  decayFactor: string
  decayFunction: string
  difficultyCoefficient: string
  firstBloodBonusPercent: string
  secondBloodBonusPercent: string
  thirdBloodBonusPercent: string
  teamRegistrationAutoApprove: boolean
  maxTeamMembers: string
  tracksEnabled: boolean
  trackNamesText: string
  roundDurationSeconds: string
  totalRounds: string
  flagFormat: string
  flagPath: string
  attackPoints: string
  serviceOnlinePoints: string
  serviceDownPenalty: string
  beenAttackedPenalty: string
  flagValidityRounds: string
  awdpAttackScorePerRound: string
  awdpDefenseScorePerRound: string
  awdpMaxAttackAttempts: string
  awdpMaxDefenseAttempts: string
  awdpAllowAttackAfterBreakSuccess: boolean
  awdpAllowDefenseAfterFixSuccess: boolean
  awdpServicePenaltyEnabled: boolean
  awdpServicePenaltyPerRound: string
  awdpViolationPenaltyEnabled: boolean
  awdpViolationPenalty: string
  awdpFixEntry: string
  awdpFixTimeoutSeconds: string
}

export interface CommandAdminCompetitionChallenge {
  id: string
  templateId?: string
  title: string
  description?: string
  descriptionFormat: string
  typeId: string
  direction: string
  deploymentType?: string | number
  exposedPort?: number | null
  flagPrefix?: string
  flagEnvironmentVariable?: string
  pointsConfig: {
    initialPoints: number
    minimumPoints: number
    decayFactor: number
    decayFunction: string
  }
  difficultyCoefficient: number
  enableBloodBonus: boolean
  awdpAttackScorePerRound?: number | null
  awdpDefenseScorePerRound?: number | null
  awdpMaxAttackAttempts?: number | null
  awdpMaxDefenseAttempts?: number | null
  awdpFixEntry?: string | null
  awdpFixTimeoutSeconds?: number | null
  hints: { id?: string, content: string, displayOrder?: number }[]
}

export interface CommandAdminCompetitionTeam {
  id: string
  name: string
  captainName: string
  memberCount: number
  inviteToken: string
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  registeredAt: string
  approvedAt?: string | null
}

export interface CommandAdminCompetitionLog {
  id: string
  level: string
  eventType: string
  message: string
  teamName?: string | null
  challengeTitle?: string | null
  createdAt: string
}

export interface CommandAdminCompetitionCheatIncident {
  id: string
  suspectTeamId: string
  suspectTeamName: string
  victimTeamName?: string | null
  challengeTitle: string
  userName: string
  reason: string
  resolved: boolean
  createdAt: string
}

const props = defineProps<{
  loading: boolean
  competitionTitle: string
  competitionStatus: string
  canOpenAwdpScreen: boolean
  activeSection: string
  competitionForm: CommandAdminCompetitionForm
  challenges: CommandAdminCompetitionChallenge[]
  loadingChallenges: boolean
  teams: CommandAdminCompetitionTeam[]
  loadingTeams: boolean
  logs: CommandAdminCompetitionLog[]
  loadingLogs: boolean
  cheats: CommandAdminCompetitionCheatIncident[]
  loadingCheats: boolean
  saving: boolean
  teamActionPending: boolean
  restartPending: boolean
  rebuildPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  switchSection: [section: string]
  save: []
  createChallenge: []
  editChallenge: [challengeId: string]
  deleteChallenge: [challengeId: string]
  restartContainer: [challengeId: string]
  approve: [teamId: string]
  reject: [teamId: string]
  toggleLock: [payload: { teamId: string, isLocked: boolean }]
  toggleBan: [payload: { teamId: string, isBanned: boolean }]
  banTeam: [teamId: string]
  rebuild: []
  back: []
  openOperations: []
  openAwdpScreen: []
  'update:competitionForm': [value: CommandAdminCompetitionForm]
}>()

const sections = [
  { key: 'settings', label: 'Settings' },
  { key: 'challenges', label: 'Challenges' },
  { key: 'teams', label: 'Teams' },
  { key: 'cheats', label: 'Cheat incidents' },
  { key: 'logs', label: 'Logs' },
]

const statusOptions = [
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Running', label: 'Running' },
  { value: 'Paused', label: 'Paused' },
  { value: 'Finished', label: 'Finished' },
]

const gameModeOptions = [
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
]

const isAwdp = computed(() => props.competitionForm.gameModeType === 'Awdp')
const isAwd = computed(() => props.competitionForm.gameModeType === 'Awd')
const isCtf = computed(() => props.competitionForm.gameModeType === 'Ctf')

function patchCompetitionForm(patch: Partial<CommandAdminCompetitionForm>) {
  emit('update:competitionForm', { ...props.competitionForm, ...patch })
}

const allDirectionsValue = '__all__'
const directionFilter = ref(allDirectionsValue)

const availableDirections = computed(() =>
  [...new Set(props.challenges.map(challenge => normalizeDirection(challenge.direction)))].sort())

const directionOptions = computed(() => [
  { value: allDirectionsValue, label: 'All directions' },
  ...availableDirections.value.map(direction => ({ value: direction, label: direction })),
])

const filteredChallenges = computed(() => directionFilter.value === allDirectionsValue
  ? props.challenges
  : props.challenges.filter(challenge => normalizeDirection(challenge.direction) === directionFilter.value))

function isStaticContainer(challenge: CommandAdminCompetitionChallenge) {
  return challenge.deploymentType === 'StaticContainer' || challenge.deploymentType === 3
}

function registrationTone(status: string): 'success' | 'danger' | 'default' {
  if (status === 'approved')
    return 'success'
  if (status === 'rejected')
    return 'danger'
  return 'default'
}

function logLevelTone(level: string): 'danger' | 'warning' | 'default' {
  const value = level.toLowerCase()
  if (value === 'error')
    return 'danger'
  if (value === 'warning')
    return 'warning'
  return 'default'
}

function formatDateTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}
</script>

<template>
  <section class="admin-detail">
    <div class="admin-detail__head">
      <div class="admin-detail__identity">
        <CommandButton label="Competitions" tone="ghost" class="admin-detail__back" @click="emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <h1 class="admin-detail__title">{{ props.competitionTitle || 'Competition' }}</h1>
      </div>
      <div class="admin-detail__actions">
        <CommandButton
          :label="props.rebuildPending ? 'Rebuilding' : 'Rebuild scoreboard'"
          tone="outline"
          :disabled="props.rebuildPending"
          @click="emit('rebuild')"
        >
          <template #icon><RefreshCw class="size-4" :class="{ 'animate-spin': props.rebuildPending }" /></template>
        </CommandButton>
        <CommandButton label="Operations" tone="outline" @click="emit('openOperations')">
          <template #icon><Settings2 class="size-4" /></template>
        </CommandButton>
        <CommandButton v-if="props.canOpenAwdpScreen" label="AWDP screen" tone="outline" @click="emit('openAwdpScreen')" />
        <CommandBadge v-if="props.competitionStatus" :label="props.competitionStatus" tone="info" />
      </div>
    </div>

    <p v-if="props.operationMessage" class="admin-detail__message" :class="`admin-detail__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel v-if="props.loading" class="admin-detail__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Loading the competition.</p>
    </CommandPanel>

    <template v-else>
      <nav class="admin-detail__tabs" aria-label="Competition sections">
        <button
          v-for="section in sections"
          :key="section.key"
          type="button"
          class="admin-detail__tab"
          :class="{ 'admin-detail__tab--active': props.activeSection === section.key }"
          @click="emit('switchSection', section.key)"
        >
          {{ section.label }}
        </button>
      </nav>

      <div class="admin-detail__layout">
        <div class="admin-detail__main">
          <!-- Settings -->
          <CommandPanel v-if="props.activeSection === 'settings'" class="admin-detail__panel">
            <div class="admin-detail__panel-head">
              <div class="admin-detail__panel-title">
                <h2>Competition settings</h2>
                <p>Update the schedule, scoring defaults, and game mode tuning.</p>
              </div>
              <CommandButton :label="props.saving ? 'Saving' : 'Save'" :disabled="props.saving" @click="emit('save')">
                <template #icon><Save class="size-4" /></template>
              </CommandButton>
            </div>

            <div class="admin-detail__grid">
              <CommandInput :model-value="props.competitionForm.title" label="Title" @update:model-value="patchCompetitionForm({ title: $event })" />
              <CommandSelect :model-value="props.competitionForm.status" label="Status" :options="statusOptions" @update:model-value="patchCompetitionForm({ status: $event })" />
              <div class="admin-detail__span">
                <CommandTextarea :model-value="props.competitionForm.description" label="Description" :rows="3" @update:model-value="patchCompetitionForm({ description: $event })" />
              </div>
              <CommandSelect :model-value="props.competitionForm.gameModeType" label="Game mode" :options="gameModeOptions" @update:model-value="patchCompetitionForm({ gameModeType: $event })" />
              <CommandInput :model-value="props.competitionForm.difficultyCoefficient" type="number" label="Difficulty coefficient" :min="0.1" :step="0.1" @update:model-value="patchCompetitionForm({ difficultyCoefficient: $event })" />
              <CommandInput :model-value="props.competitionForm.maxTeamMembers" type="number" label="Max team members" :min="1" @update:model-value="patchCompetitionForm({ maxTeamMembers: $event })" />
              <CommandCheckbox :checked="props.competitionForm.teamRegistrationAutoApprove" label="Auto approve team registrations" @update:checked="patchCompetitionForm({ teamRegistrationAutoApprove: $event })" />
              <CommandCheckbox :checked="props.competitionForm.tracksEnabled" label="Enable tracks" @update:checked="patchCompetitionForm({ tracksEnabled: $event })" />
              <div v-if="props.competitionForm.tracksEnabled" class="admin-detail__span">
                <CommandTextarea :model-value="props.competitionForm.trackNamesText" label="Track names" :rows="3" placeholder="One track per line, or comma separated" @update:model-value="patchCompetitionForm({ trackNamesText: $event })" />
              </div>
              <CommandInput :model-value="props.competitionForm.startTime" type="datetime-local" label="Start time" @update:model-value="patchCompetitionForm({ startTime: $event })" />
              <CommandInput :model-value="props.competitionForm.endTime" type="datetime-local" label="End time" @update:model-value="patchCompetitionForm({ endTime: $event })" />
            </div>

            <CommandPanel v-if="isCtf" class="admin-detail__subpanel">
              <div class="admin-detail__grid admin-detail__grid--three">
                <CommandInput :model-value="props.competitionForm.firstBloodBonusPercent" type="number" label="First blood bonus (%)" :min="0" :step="1" @update:model-value="patchCompetitionForm({ firstBloodBonusPercent: $event })" />
                <CommandInput :model-value="props.competitionForm.secondBloodBonusPercent" type="number" label="Second blood bonus (%)" :min="0" :step="1" @update:model-value="patchCompetitionForm({ secondBloodBonusPercent: $event })" />
                <CommandInput :model-value="props.competitionForm.thirdBloodBonusPercent" type="number" label="Third blood bonus (%)" :min="0" :step="1" @update:model-value="patchCompetitionForm({ thirdBloodBonusPercent: $event })" />
              </div>
            </CommandPanel>

            <CommandPanel v-if="isAwd || isAwdp" class="admin-detail__subpanel">
              <div class="admin-detail__grid admin-detail__grid--four">
                <CommandInput :model-value="props.competitionForm.roundDurationSeconds" type="number" label="Round duration (seconds)" :min="1" placeholder="300" @update:model-value="patchCompetitionForm({ roundDurationSeconds: $event })" />
                <CommandInput :model-value="props.competitionForm.totalRounds" type="number" label="Total rounds" :min="1" placeholder="10" @update:model-value="patchCompetitionForm({ totalRounds: $event })" />
                <template v-if="isAwd">
                  <CommandInput :model-value="props.competitionForm.attackPoints" type="number" label="Attack points" :min="0" placeholder="50" @update:model-value="patchCompetitionForm({ attackPoints: $event })" />
                  <CommandInput :model-value="props.competitionForm.serviceOnlinePoints" type="number" label="Service online points" :min="0" placeholder="100" @update:model-value="patchCompetitionForm({ serviceOnlinePoints: $event })" />
                  <CommandInput :model-value="props.competitionForm.serviceDownPenalty" type="number" label="Service down penalty" :min="0" placeholder="50" @update:model-value="patchCompetitionForm({ serviceDownPenalty: $event })" />
                  <CommandInput :model-value="props.competitionForm.beenAttackedPenalty" type="number" label="Been attacked penalty" :min="0" placeholder="50" @update:model-value="patchCompetitionForm({ beenAttackedPenalty: $event })" />
                  <CommandInput :model-value="props.competitionForm.flagValidityRounds" type="number" label="Flag validity rounds" :min="1" placeholder="2" @update:model-value="patchCompetitionForm({ flagValidityRounds: $event })" />
                  <CommandInput :model-value="props.competitionForm.flagFormat" label="Flag format" placeholder="flag{{{0}}}" @update:model-value="patchCompetitionForm({ flagFormat: $event })" />
                  <CommandInput :model-value="props.competitionForm.flagPath" label="Flag path" placeholder="/flag/flag.txt" @update:model-value="patchCompetitionForm({ flagPath: $event })" />
                </template>
              </div>
            </CommandPanel>

            <CommandPanel v-if="isAwdp" class="admin-detail__subpanel">
              <div class="admin-detail__grid admin-detail__grid--three">
                <CommandInput :model-value="props.competitionForm.awdpAttackScorePerRound" type="number" label="Attack score per round" :min="0" @update:model-value="patchCompetitionForm({ awdpAttackScorePerRound: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpDefenseScorePerRound" type="number" label="Defense score per round" :min="0" @update:model-value="patchCompetitionForm({ awdpDefenseScorePerRound: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpFixTimeoutSeconds" type="number" label="Fix timeout (seconds)" :min="1" @update:model-value="patchCompetitionForm({ awdpFixTimeoutSeconds: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpMaxAttackAttempts" type="number" label="Max attack attempts" :min="1" @update:model-value="patchCompetitionForm({ awdpMaxAttackAttempts: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpMaxDefenseAttempts" type="number" label="Max defense attempts" :min="1" @update:model-value="patchCompetitionForm({ awdpMaxDefenseAttempts: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpFixEntry" label="Fix entry" placeholder="fix.sh" @update:model-value="patchCompetitionForm({ awdpFixEntry: $event })" />
              </div>
            </CommandPanel>

            <CommandPanel v-if="isAwdp" class="admin-detail__subpanel">
              <div class="admin-detail__grid admin-detail__grid--four">
                <CommandInput :model-value="props.competitionForm.awdpServicePenaltyPerRound" type="number" label="Service penalty per round" :min="0" :disabled="!props.competitionForm.awdpServicePenaltyEnabled" @update:model-value="patchCompetitionForm({ awdpServicePenaltyPerRound: $event })" />
                <CommandInput :model-value="props.competitionForm.awdpViolationPenalty" type="number" label="Violation penalty" :min="0" :disabled="!props.competitionForm.awdpViolationPenaltyEnabled" @update:model-value="patchCompetitionForm({ awdpViolationPenalty: $event })" />
                <CommandCheckbox :checked="props.competitionForm.awdpAllowAttackAfterBreakSuccess" label="Allow repeated attacks" @update:checked="patchCompetitionForm({ awdpAllowAttackAfterBreakSuccess: $event })" />
                <CommandCheckbox :checked="props.competitionForm.awdpAllowDefenseAfterFixSuccess" label="Allow repeated defenses" @update:checked="patchCompetitionForm({ awdpAllowDefenseAfterFixSuccess: $event })" />
                <CommandCheckbox :checked="props.competitionForm.awdpServicePenaltyEnabled" label="Enable service penalty" @update:checked="patchCompetitionForm({ awdpServicePenaltyEnabled: $event })" />
                <CommandCheckbox :checked="props.competitionForm.awdpViolationPenaltyEnabled" label="Enable violation penalty" @update:checked="patchCompetitionForm({ awdpViolationPenaltyEnabled: $event })" />
              </div>
            </CommandPanel>
          </CommandPanel>

          <!-- Challenges -->
          <CommandPanel v-if="props.activeSection === 'challenges'" class="admin-detail__panel">
            <div class="admin-detail__panel-head">
              <div class="admin-detail__panel-title">
                <h2>Deployed challenges</h2>
                <p>Manage the challenges bound to this competition.</p>
              </div>
              <CommandButton label="Deploy challenge" @click="emit('createChallenge')">
                <template #icon><Plus class="size-4" /></template>
              </CommandButton>
            </div>

            <div class="admin-detail__challenges-toolbar">
              <CommandSelect v-model="directionFilter" label="Direction" :options="directionOptions" />
              <p v-if="!props.loadingChallenges" class="admin-detail__challenge-count">
                {{ filteredChallenges.length }} challenge(s)
              </p>
            </div>

            <div v-if="props.loadingChallenges" class="admin-detail__inline-state" aria-busy="true">
              <CommandSignal label="Synchronizing" tone="info" />
              <span>Loading challenges.</span>
            </div>
            <p v-else-if="filteredChallenges.length === 0" class="admin-detail__empty">No challenges deployed.</p>
            <div v-else class="admin-detail__deployed">
              <div
                v-for="challenge in filteredChallenges"
                :key="challenge.id"
                class="admin-detail__deployed-row"
              >
                <div class="admin-detail__deployed-identity">
                  <strong>{{ challenge.title }}</strong>
                  <span>{{ normalizeDirection(challenge.direction) }} · {{ challenge.typeId }} · {{ challenge.pointsConfig.minimumPoints }} → {{ challenge.pointsConfig.initialPoints }}</span>
                </div>
                <div class="admin-detail__row-actions">
                  <CommandButton v-if="isStaticContainer(challenge)" label="Restart" tone="ghost" :disabled="props.restartPending" @click="emit('restartContainer', challenge.id)">
                    <template #icon><RotateCw class="size-4" :class="{ 'animate-spin': props.restartPending }" /></template>
                  </CommandButton>
                  <CommandButton label="Edit" tone="ghost" @click="emit('editChallenge', challenge.id)">
                    <template #icon><Edit3 class="size-4" /></template>
                  </CommandButton>
                  <CommandButton label="Remove" tone="ghost" class="admin-detail__danger-action" @click="emit('deleteChallenge', challenge.id)">
                    <template #icon><Trash2 class="size-4" /></template>
                  </CommandButton>
                </div>
              </div>
            </div>
          </CommandPanel>

          <!-- Teams -->
          <CommandPanel v-if="props.activeSection === 'teams'" class="admin-detail__panel">
            <div class="admin-detail__panel-title">
              <h2>Team review</h2>
              <p>Approve registrations, lock rosters, and ban offending teams.</p>
            </div>
            <div v-if="props.loadingTeams" class="admin-detail__inline-state" aria-busy="true">
              <CommandSignal label="Synchronizing" tone="info" />
              <span>Loading teams.</span>
            </div>
            <p v-else-if="props.teams.length === 0" class="admin-detail__empty">No teams registered yet.</p>
            <div v-else class="admin-detail__table-wrap">
              <table class="admin-detail__table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Members</th>
                    <th>Track</th>
                    <th>Status</th>
                    <th>Locked</th>
                    <th>Banned</th>
                    <th class="admin-detail__table-actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="team in props.teams" :key="team.id">
                    <td>
                      <div class="admin-detail__team-name">{{ team.name }}</div>
                      <div class="admin-detail__team-captain">{{ team.captainName }}</div>
                      <code class="admin-detail__team-token">{{ team.inviteToken }}</code>
                    </td>
                    <td>{{ team.memberCount }} / {{ props.competitionForm.maxTeamMembers }}</td>
                    <td>{{ team.trackName || '-' }}</td>
                    <td><CommandBadge :label="team.registrationStatus" :tone="registrationTone(team.registrationStatus)" /></td>
                    <td><CommandBadge :label="team.isLocked ? 'Locked' : 'Unlocked'" :tone="team.isLocked ? 'warning' : 'default'" /></td>
                    <td><CommandBadge :label="team.isBanned ? 'Banned' : 'Normal'" :tone="team.isBanned ? 'danger' : 'default'" /></td>
                    <td>
                      <div class="admin-detail__row-actions">
                        <CommandButton v-if="team.registrationStatus !== 'approved'" label="Approve" tone="ghost" :disabled="props.teamActionPending" @click="emit('approve', team.id)">
                          <template #icon><Check class="size-4" /></template>
                        </CommandButton>
                        <CommandButton v-if="team.registrationStatus !== 'rejected'" label="Reject" tone="ghost" class="admin-detail__danger-action" :disabled="props.teamActionPending" @click="emit('reject', team.id)">
                          <template #icon><X class="size-4" /></template>
                        </CommandButton>
                        <CommandButton :label="team.isLocked ? 'Unlock' : 'Lock'" tone="ghost" :disabled="props.teamActionPending" @click="emit('toggleLock', { teamId: team.id, isLocked: !team.isLocked })">
                          <template #icon>
                            <Unlock v-if="team.isLocked" class="size-4" />
                            <Lock v-else class="size-4" />
                          </template>
                        </CommandButton>
                        <CommandButton :label="team.isBanned ? 'Unban' : 'Ban'" tone="ghost" :class="{ 'admin-detail__danger-action': !team.isBanned }" :disabled="props.teamActionPending" @click="emit('toggleBan', { teamId: team.id, isBanned: team.isBanned })">
                          <template #icon><ShieldAlert class="size-4" /></template>
                        </CommandButton>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </CommandPanel>

          <!-- Cheat incidents -->
          <CommandPanel v-if="props.activeSection === 'cheats'" class="admin-detail__panel">
            <div class="admin-detail__panel-title">
              <h2>Cheat incidents</h2>
              <p>Flag sharing detections reported by the anti-cheat pipeline.</p>
            </div>
            <div v-if="props.loadingCheats" class="admin-detail__inline-state" aria-busy="true">
              <CommandSignal label="Synchronizing" tone="info" />
              <span>Loading cheat incidents.</span>
            </div>
            <p v-else-if="props.cheats.length === 0" class="admin-detail__empty">No cheat incidents recorded.</p>
            <div v-else class="admin-detail__incidents">
              <CommandPanel v-for="incident in props.cheats" :key="incident.id" class="admin-detail__incident">
                <div class="admin-detail__incident-body">
                  <strong>{{ incident.suspectTeamName }} → {{ incident.victimTeamName || '-' }}</strong>
                  <span>{{ incident.challengeTitle }} · {{ incident.userName }} · {{ formatDateTime(incident.createdAt) }}</span>
                </div>
                <CommandButton label="Ban team" class="admin-detail__danger-action admin-detail__danger-action--solid" :disabled="props.teamActionPending" @click="emit('banTeam', incident.suspectTeamId)">
                  <template #icon><ShieldAlert class="size-4" /></template>
                </CommandButton>
              </CommandPanel>
            </div>
          </CommandPanel>

          <!-- Logs -->
          <CommandPanel v-if="props.activeSection === 'logs'" class="admin-detail__panel">
            <div class="admin-detail__panel-title">
              <h2>Competition logs</h2>
              <p>Operational events recorded for this competition.</p>
            </div>
            <div v-if="props.loadingLogs" class="admin-detail__inline-state" aria-busy="true">
              <CommandSignal label="Synchronizing" tone="info" />
              <span>Loading logs.</span>
            </div>
            <div v-else class="admin-detail__logs">
              <CommandPanel v-for="log in props.logs" :key="log.id" class="admin-detail__log">
                <div class="admin-detail__log-head">
                  <CommandBadge :label="log.eventType" :tone="logLevelTone(log.level)" />
                  <span class="admin-detail__log-time">{{ formatDateTime(log.createdAt) }}</span>
                </div>
                <p class="admin-detail__log-message">{{ log.message }}</p>
                <p v-if="log.teamName || log.challengeTitle" class="admin-detail__log-meta">{{ log.teamName || '-' }} · {{ log.challengeTitle || '-' }}</p>
              </CommandPanel>
              <p v-if="props.logs.length === 0" class="admin-detail__empty">No logs recorded.</p>
            </div>
          </CommandPanel>
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.admin-detail { display: grid; align-content: start; gap: 16px; }
.admin-detail__head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 12px; }
.admin-detail__identity { display: grid; gap: 6px; }
.admin-detail__back { justify-self: start; margin-left: -8px; }
.admin-detail__title { margin: 0; color: var(--v2-text); font-size: 22px; font-weight: 600; }
.admin-detail__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.admin-detail__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-detail__message--danger { color: var(--v2-danger); }
.admin-detail__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-detail__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-detail__tabs { display: flex; flex-wrap: wrap; gap: 6px; border-radius: 14px; padding: 6px; background: var(--v2-surface); box-shadow: var(--v2-inset); width: fit-content; max-width: 100%; }
.admin-detail__tab { border: 0; border-radius: 10px; padding: 8px 14px; color: var(--v2-text-muted); background: transparent; cursor: pointer; font-family: inherit; font-size: 12px; font-weight: 600; }
.admin-detail__tab--active { color: var(--v2-primary); background: var(--v2-canvas); box-shadow: var(--v2-raised-sm); }
.admin-detail__layout { display: grid; align-items: start; gap: 16px; }
.admin-detail__main { display: grid; align-content: start; gap: 16px; min-width: 0; }
.admin-detail__panel { display: grid; align-content: start; gap: 16px; padding: 18px; }
.admin-detail__panel-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 12px; }
.admin-detail__panel-title { display: grid; gap: 4px; }
.admin-detail__panel-title h2 { margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; }
.admin-detail__panel-title p { margin: 0; color: var(--v2-text-muted); font-size: 12px; }
.admin-detail__grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-detail__grid--three { grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); }
.admin-detail__grid--four { grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
.admin-detail__span { grid-column: 1 / -1; }
.admin-detail__subpanel { padding: 14px; }
.admin-detail__challenges-toolbar { display: flex; flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 12px; }
.admin-detail__challenge-count { margin: 0; color: var(--v2-text-faint); font-size: 12px; }
.admin-detail__inline-state { display: flex; align-items: center; gap: 10px; border-radius: 12px; padding: 18px 14px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-detail__empty { margin: 0; border-radius: 12px; padding: 20px 14px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; text-align: center; }
.admin-detail__table-wrap { overflow-x: auto; }
.admin-detail__table { width: 100%; border-collapse: collapse; font-size: 12px; }
.admin-detail__table th { padding: 8px 10px; color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.08em; text-align: left; }
.admin-detail__table td { padding: 10px; color: var(--v2-text-muted); vertical-align: top; }
.admin-detail__table tbody tr { border-radius: 10px; }
.admin-detail__table tbody tr:hover { background: var(--v2-surface); }
.admin-detail__table-actions { text-align: right !important; }
.admin-detail__team-name { color: var(--v2-text); font-weight: 600; }
.admin-detail__team-captain { color: var(--v2-text-faint); font-size: 11px; }
.admin-detail__team-token { display: block; margin-top: 3px; color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.admin-detail__row-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 4px; }
.admin-detail__danger-action { color: var(--v2-danger); }
.admin-detail__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-detail__incidents { display: grid; gap: 10px; }
.admin-detail__incident { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 14px; }
.admin-detail__incident-body { display: grid; gap: 3px; }
.admin-detail__incident-body strong { color: var(--v2-text); font-size: 13px; font-weight: 600; }
.admin-detail__incident-body span { color: var(--v2-text-faint); font-size: 11px; }
.admin-detail__logs { display: grid; max-height: 380px; gap: 10px; overflow-y: auto; padding-right: 4px; }
.admin-detail__log { display: grid; gap: 8px; padding: 12px 14px; }
.admin-detail__log-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; }
.admin-detail__log-time { color: var(--v2-text-faint); font-size: 11px; }
.admin-detail__log-message { margin: 0; color: var(--v2-text); font-size: 13px; }
.admin-detail__log-meta { margin: 0; color: var(--v2-text-faint); font-size: 11px; }
.admin-detail__deployed { display: grid; gap: 8px; }
.admin-detail__deployed-row { display: flex; align-items: center; justify-content: space-between; gap: 10px; border-radius: 12px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-detail__deployed-identity { display: grid; min-width: 0; gap: 3px; }
.admin-detail__deployed-identity strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-detail__deployed-identity span { color: var(--v2-text-faint); font-size: 11px; }
</style>
