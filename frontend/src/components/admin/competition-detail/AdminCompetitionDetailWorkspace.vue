<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Loader2, RefreshCw } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionChallengesPanel from '@/components/admin/competition-detail/AdminCompetitionChallengesPanel.vue'
import AdminCompetitionCheatIncidentsPanel from '@/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue'
import AdminCompetitionLogsPanel from '@/components/admin/competition-detail/AdminCompetitionLogsPanel.vue'
import AdminCompetitionSettingsPanel from '@/components/admin/competition-detail/AdminCompetitionSettingsPanel.vue'
import AdminCompetitionTeamsPanel from '@/components/admin/competition-detail/AdminCompetitionTeamsPanel.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs'

interface PointsConfigDto {
  initialPoints: number
  minimumPoints: number
  decayFactor: number
  decayFunction: string
}

interface CompetitionDto {
  id: string
  title: string
  description?: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
  defaultPointsConfig: PointsConfigDto
  difficultyCoefficient: number
  firstBloodBonusPercent: number
  secondBloodBonusPercent: number
  thirdBloodBonusPercent: number
  teamRegistrationAutoApprove: boolean
  maxTeamMembers: number
  tracksEnabled: boolean
  trackNames: string[]
  roundDurationSeconds?: number | null
  totalRounds?: number | null
  flagFormat?: string | null
  flagPath?: string | null
  attackPoints?: number | null
  serviceOnlinePoints?: number | null
  serviceDownPenalty?: number | null
  beenAttackedPenalty?: number | null
  flagValidityRounds?: number | null
  awdpAttackScorePerRound?: number | null
  awdpDefenseScorePerRound?: number | null
  awdpMaxAttackAttempts?: number | null
  awdpMaxDefenseAttempts?: number | null
  awdpAllowAttackAfterBreakSuccess?: boolean | null
  awdpAllowDefenseAfterFixSuccess?: boolean | null
  awdpServicePenaltyEnabled?: boolean | null
  awdpServicePenaltyPerRound?: number | null
  awdpViolationPenaltyEnabled?: boolean | null
  awdpViolationPenalty?: number | null
  awdpFixEntry?: string | null
  awdpFixTimeoutSeconds?: number | null
}

interface CompetitionTeamDto {
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

interface CompetitionLogDto {
  id: string
  level: string
  eventType: string
  message: string
  teamName?: string | null
  challengeTitle?: string | null
  submittedFlag?: string | null
  createdAt: string
}

interface CheatIncidentDto {
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

const route = useRoute()
const router = useRouter()
const qc = useQueryClient()
const { t } = useI18n()
const competitionId = computed(() => String(route.params.id))
const competitionDetailSections = [
  { key: 'settings', labelKey: 'admin.competitionDetail.navSettings' },
  { key: 'challenges', labelKey: 'admin.competitionDetail.navChallenges' },
  { key: 'teams', labelKey: 'admin.competitionDetail.navTeams' },
  { key: 'cheats', labelKey: 'admin.competitionDetail.navCheats' },
  { key: 'logs', labelKey: 'admin.competitionDetail.navLogs' },
] as const
type CompetitionDetailSection = typeof competitionDetailSections[number]['key']
const trackNamesSeparatorPattern = /\r?\n|,/

const competitionForm = reactive({
  title: '',
  description: '',
  gameModeType: 'Ctf',
  status: 'Draft',
  startTime: '',
  endTime: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'sigmoid',
  difficultyCoefficient: 1,
  firstBloodBonusPercent: 0,
  secondBloodBonusPercent: 0,
  thirdBloodBonusPercent: 0,
  teamRegistrationAutoApprove: true,
  maxTeamMembers: 5,
  tracksEnabled: false,
  trackNamesText: '',
  roundDurationSeconds: undefined as number | undefined,
  totalRounds: undefined as number | undefined,
  flagFormat: '',
  flagPath: '',
  attackPoints: undefined as number | undefined,
  serviceOnlinePoints: undefined as number | undefined,
  serviceDownPenalty: undefined as number | undefined,
  beenAttackedPenalty: undefined as number | undefined,
  flagValidityRounds: undefined as number | undefined,
  awdpAttackScorePerRound: undefined as number | undefined,
  awdpDefenseScorePerRound: undefined as number | undefined,
  awdpMaxAttackAttempts: undefined as number | undefined,
  awdpMaxDefenseAttempts: undefined as number | undefined,
  awdpAllowAttackAfterBreakSuccess: false,
  awdpAllowDefenseAfterFixSuccess: false,
  awdpServicePenaltyEnabled: false,
  awdpServicePenaltyPerRound: undefined as number | undefined,
  awdpViolationPenaltyEnabled: false,
  awdpViolationPenalty: undefined as number | undefined,
  awdpFixEntry: 'fix.sh',
  awdpFixTimeoutSeconds: undefined as number | undefined,
})

function toDateTimeLocal(value: string) {
  if (!value)
    return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function optionalNumber(value: number | string | undefined | null) {
  if (value === undefined || value === null || value === '')
    return undefined
  return Number(value)
}

function numberOrDefault(value: number | string | undefined | null, fallback: number) {
  if (value === undefined || value === null || value === '')
    return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function competitionPayload() {
  return {
    title: competitionForm.title.trim(),
    description: competitionForm.description.trim() || undefined,
    gameModeType: competitionForm.gameModeType,
    status: competitionForm.status,
    startTime: new Date(competitionForm.startTime).toISOString(),
    endTime: new Date(competitionForm.endTime).toISOString(),
    defaultPointsConfig: {
      initialPoints: numberOrDefault(competitionForm.initialPoints, 500),
      minimumPoints: numberOrDefault(competitionForm.minimumPoints, 100),
      decayFactor: numberOrDefault(competitionForm.decayFactor, 450),
      decayFunction: competitionForm.decayFunction || 'sigmoid',
    },
    difficultyCoefficient: numberOrDefault(competitionForm.difficultyCoefficient, 1),
    firstBloodBonusPercent: numberOrDefault(competitionForm.firstBloodBonusPercent, 0),
    secondBloodBonusPercent: numberOrDefault(competitionForm.secondBloodBonusPercent, 0),
    thirdBloodBonusPercent: numberOrDefault(competitionForm.thirdBloodBonusPercent, 0),
    teamRegistrationAutoApprove: competitionForm.teamRegistrationAutoApprove,
    maxTeamMembers: numberOrDefault(competitionForm.maxTeamMembers, 5),
    tracksEnabled: competitionForm.tracksEnabled,
    trackNames: competitionForm.trackNamesText.split(trackNamesSeparatorPattern).map(item => item.trim()).filter(Boolean),
    roundDurationSeconds: optionalNumber(competitionForm.roundDurationSeconds),
    totalRounds: optionalNumber(competitionForm.totalRounds),
    flagFormat: competitionForm.flagFormat.trim() || undefined,
    flagPath: competitionForm.flagPath.trim() || undefined,
    attackPoints: optionalNumber(competitionForm.attackPoints),
    serviceOnlinePoints: optionalNumber(competitionForm.serviceOnlinePoints),
    serviceDownPenalty: optionalNumber(competitionForm.serviceDownPenalty),
    beenAttackedPenalty: optionalNumber(competitionForm.beenAttackedPenalty),
    flagValidityRounds: optionalNumber(competitionForm.flagValidityRounds),
    awdpAttackScorePerRound: optionalNumber(competitionForm.awdpAttackScorePerRound),
    awdpDefenseScorePerRound: optionalNumber(competitionForm.awdpDefenseScorePerRound),
    awdpMaxAttackAttempts: optionalNumber(competitionForm.awdpMaxAttackAttempts),
    awdpMaxDefenseAttempts: optionalNumber(competitionForm.awdpMaxDefenseAttempts),
    awdpAllowAttackAfterBreakSuccess: competitionForm.awdpAllowAttackAfterBreakSuccess,
    awdpAllowDefenseAfterFixSuccess: competitionForm.awdpAllowDefenseAfterFixSuccess,
    awdpServicePenaltyEnabled: competitionForm.awdpServicePenaltyEnabled,
    awdpServicePenaltyPerRound: optionalNumber(competitionForm.awdpServicePenaltyPerRound),
    awdpViolationPenaltyEnabled: competitionForm.awdpViolationPenaltyEnabled,
    awdpViolationPenalty: optionalNumber(competitionForm.awdpViolationPenalty),
    awdpFixEntry: competitionForm.awdpFixEntry.trim() || undefined,
    awdpFixTimeoutSeconds: optionalNumber(competitionForm.awdpFixTimeoutSeconds),
  }
}

const { data: competition, isLoading: loadingCompetition } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetition(competitionId.value)),
  queryFn: () => adminApi.competition<CompetitionDto>(competitionId.value),
})

const { data: competitionTeams, isLoading: loadingTeams } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionTeams(competitionId.value)),
  queryFn: () => adminApi.competitionTeams<CompetitionTeamDto[]>(competitionId.value),
})

const { data: competitionLogs, isLoading: loadingLogs } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionLogs(competitionId.value)),
  queryFn: () => adminApi.competitionLogs<CompetitionLogDto[]>(competitionId.value),
})

const { data: cheatIncidents, isLoading: loadingCheatIncidents } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionCheatIncidents(competitionId.value)),
  queryFn: () => adminApi.competitionCheatIncidents<CheatIncidentDto[]>(competitionId.value),
})

watch(competition, (value) => {
  if (!value)
    return
  competitionForm.title = value.title
  competitionForm.description = value.description ?? ''
  competitionForm.gameModeType = value.gameModeType
  competitionForm.status = value.status
  competitionForm.startTime = toDateTimeLocal(value.startTime)
  competitionForm.endTime = toDateTimeLocal(value.endTime)
  competitionForm.initialPoints = value.defaultPointsConfig?.initialPoints ?? 500
  competitionForm.minimumPoints = value.defaultPointsConfig?.minimumPoints ?? 100
  competitionForm.decayFactor = value.defaultPointsConfig?.decayFactor ?? 450
  competitionForm.decayFunction = value.defaultPointsConfig?.decayFunction ?? 'sigmoid'
  competitionForm.difficultyCoefficient = value.difficultyCoefficient ?? 1
  competitionForm.firstBloodBonusPercent = value.firstBloodBonusPercent ?? 0
  competitionForm.secondBloodBonusPercent = value.secondBloodBonusPercent ?? 0
  competitionForm.thirdBloodBonusPercent = value.thirdBloodBonusPercent ?? 0
  competitionForm.teamRegistrationAutoApprove = value.teamRegistrationAutoApprove ?? true
  competitionForm.maxTeamMembers = value.maxTeamMembers ?? 5
  competitionForm.tracksEnabled = value.tracksEnabled ?? false
  competitionForm.trackNamesText = (value.trackNames ?? []).join('\n')
  competitionForm.roundDurationSeconds = value.roundDurationSeconds ?? undefined
  competitionForm.totalRounds = value.totalRounds ?? undefined
  competitionForm.flagFormat = value.flagFormat ?? ''
  competitionForm.flagPath = value.flagPath ?? ''
  competitionForm.attackPoints = value.attackPoints ?? undefined
  competitionForm.serviceOnlinePoints = value.serviceOnlinePoints ?? undefined
  competitionForm.serviceDownPenalty = value.serviceDownPenalty ?? undefined
  competitionForm.beenAttackedPenalty = value.beenAttackedPenalty ?? undefined
  competitionForm.flagValidityRounds = value.flagValidityRounds ?? undefined
  competitionForm.awdpAttackScorePerRound = value.awdpAttackScorePerRound ?? undefined
  competitionForm.awdpDefenseScorePerRound = value.awdpDefenseScorePerRound ?? undefined
  competitionForm.awdpMaxAttackAttempts = value.awdpMaxAttackAttempts ?? undefined
  competitionForm.awdpMaxDefenseAttempts = value.awdpMaxDefenseAttempts ?? undefined
  competitionForm.awdpAllowAttackAfterBreakSuccess = value.awdpAllowAttackAfterBreakSuccess ?? false
  competitionForm.awdpAllowDefenseAfterFixSuccess = value.awdpAllowDefenseAfterFixSuccess ?? false
  competitionForm.awdpServicePenaltyEnabled = value.awdpServicePenaltyEnabled ?? false
  competitionForm.awdpServicePenaltyPerRound = value.awdpServicePenaltyPerRound ?? undefined
  competitionForm.awdpViolationPenaltyEnabled = value.awdpViolationPenaltyEnabled ?? false
  competitionForm.awdpViolationPenalty = value.awdpViolationPenalty ?? undefined
  competitionForm.awdpFixEntry = value.awdpFixEntry ?? 'fix.sh'
  competitionForm.awdpFixTimeoutSeconds = value.awdpFixTimeoutSeconds ?? undefined
}, { immediate: true })

const saveCompetitionMutation = useMutation({
  mutationFn: () => adminApi.updateCompetition(competitionId.value, competitionPayload()),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetition(competitionId.value) })
    toast.success(t('admin.competitionDetail.saveCompetitionSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.saveCompetitionError')),
})

const approveTeamMutation = useMutation({
  mutationFn: (teamId: string) => adminApi.approveCompetitionTeam(competitionId.value, teamId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamApproved'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const rejectTeamMutation = useMutation({
  mutationFn: (teamId: string) => adminApi.rejectCompetitionTeam(competitionId.value, teamId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamRejected'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const lockTeamMutation = useMutation({
  mutationFn: ({ teamId, isLocked }: { teamId: string, isLocked: boolean }) =>
    adminApi.setCompetitionTeamLock(competitionId.value, teamId, isLocked),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamLockUpdated'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const banTeamMutation = useMutation({
  mutationFn: (teamId: string) => adminApi.banCompetitionTeam(competitionId.value, teamId, 'suspected_cheat'),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamBanned'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const unbanTeamMutation = useMutation({
  mutationFn: (teamId: string) => adminApi.unbanCompetitionTeam(competitionId.value, teamId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamUnbanned'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const rebuildScoreboardMutation = useMutation({
  mutationFn: () => adminApi.rebuildScoreboard(competitionId.value) as Promise<{ rows?: number }>,
  onSuccess: (result) => {
    qc.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) })
    toast.success(result.rows === undefined
      ? t('admin.competitionDetail.scoreboardRebuilt')
      : t('admin.competitionDetail.scoreboardRebuiltWithTeams', { count: result.rows }))
  },
  onError: () => toast.error(t('admin.competitionDetail.scoreboardRebuildError')),
})

const activeSection = computed<CompetitionDetailSection>(() => {
  const section = route.query.section
  if (typeof section === 'string' && competitionDetailSections.some(item => item.key === section))
    return section as CompetitionDetailSection

  return 'settings'
})

const canOpenAwdpScreen = computed(() =>
  (competition.value?.gameModeType ?? competitionForm.gameModeType).toLowerCase() === 'awdp')

function sectionRoute(section: CompetitionDetailSection) {
  return {
    name: 'admin-competition-detail',
    params: { id: competitionId.value },
    query: { ...route.query, section },
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="router.push({ name: 'admin-competitions' })">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitions.title') }}
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">
            {{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}
          </h2>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.competitionDetail.subtitle') }}
          </p>
        </div>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="rebuildScoreboardMutation.isPending.value"
          @click="rebuildScoreboardMutation.mutate()"
        >
          <Loader2 v-if="rebuildScoreboardMutation.isPending.value" class="size-4 animate-spin" />
          <RefreshCw v-else class="size-4" />
          {{ t('admin.competitionDetail.rebuildScoreboard') }}
        </Button>
        <Badge v-if="competition?.status" variant="outline" class="capitalize">
          {{ t(`competitions.status.${competition.status.toLowerCase()}`, competition.status) }}
        </Badge>
      </div>
    </div>

    <div v-if="loadingCompetition" class="flex flex-col items-center justify-center py-12 text-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <Tabs
      v-else
      :model-value="activeSection"
      @update:model-value="(value: string | undefined) => value && router.replace(sectionRoute(value as CompetitionDetailSection))"
    >
      <TabsList class="h-auto flex-wrap justify-start">
        <TabsTrigger
          v-for="section in competitionDetailSections.slice(0, 3)"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
        <Button variant="ghost" size="sm" class="h-8 rounded-none px-3" as-child>
          <RouterLink :to="{ name: 'admin-competition-operations', params: { id: competitionId } }">
            {{ t('admin.competitionDetail.navOperations') }}
          </RouterLink>
        </Button>
        <Button v-if="canOpenAwdpScreen" variant="ghost" size="sm" class="h-8 rounded-none px-3" as-child>
          <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
            {{ t('awdp.screenEntry') }}
          </RouterLink>
        </Button>
        <TabsTrigger
          v-for="section in competitionDetailSections.slice(3)"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
      </TabsList>

      <div class="grid gap-6">
        <section class="space-y-6">
          <TabsContent value="settings">
            <AdminCompetitionSettingsPanel
              v-if="activeSection === 'settings'"
              :competition-form="competitionForm"
              :saving="saveCompetitionMutation.isPending.value"
              @save="saveCompetitionMutation.mutate()"
            />
          </TabsContent>

          <TabsContent value="challenges">
            <AdminCompetitionChallengesPanel
              v-if="activeSection === 'challenges'"
              :competition-id="competitionId"
            />
          </TabsContent>

          <TabsContent value="teams">
            <AdminCompetitionTeamsPanel
              v-if="activeSection === 'teams'"
              :competition-teams="competitionTeams"
              :loading-teams="loadingTeams"
              :max-team-members="competitionForm.maxTeamMembers"
              @approve="approveTeamMutation.mutate($event)"
              @reject="rejectTeamMutation.mutate($event)"
              @toggle-lock="lockTeamMutation.mutate($event)"
              @toggle-ban="$event.isBanned ? unbanTeamMutation.mutate($event.teamId) : banTeamMutation.mutate($event.teamId)"
            />
          </TabsContent>

          <TabsContent value="cheats">
            <AdminCompetitionCheatIncidentsPanel
              v-if="activeSection === 'cheats'"
              :cheat-incidents="cheatIncidents"
              :loading-cheat-incidents="loadingCheatIncidents"
              @ban="banTeamMutation.mutate($event)"
            />
          </TabsContent>

          <TabsContent value="logs">
            <AdminCompetitionLogsPanel
              v-if="activeSection === 'logs'"
              :competition-logs="competitionLogs"
              :loading-logs="loadingLogs"
            />
          </TabsContent>
        </section>
      </div>
    </Tabs>
  </div>
</template>
