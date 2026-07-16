<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Loader2, RefreshCw, Save, Trash2 } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionChallengeBinder from '@/components/admin/competition-detail/AdminCompetitionChallengeBinder.vue'
import AdminCompetitionCheatIncidentsPanel from '@/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue'
import AdminCompetitionLogsPanel from '@/components/admin/competition-detail/AdminCompetitionLogsPanel.vue'
import AdminCompetitionSettingsPanel from '@/components/admin/competition-detail/AdminCompetitionSettingsPanel.vue'
import AdminCompetitionTeamsPanel from '@/components/admin/competition-detail/AdminCompetitionTeamsPanel.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'

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

interface ChallengeTemplateDto {
  id: string
  title: string
  description?: string
  typeId: string
}

interface ChallengeHintDto {
  id?: string
  content: string
  displayOrder?: number
}

interface CompetitionChallengeDto {
  id: string
  templateId?: string
  title: string
  description?: string
  descriptionFormat: string
  typeId: string
  deploymentType?: string | number
  exposedPort?: number | null
  flagPrefix?: string
  flagEnvironmentVariable?: string
  pointsConfig: PointsConfigDto
  difficultyCoefficient: number
  enableBloodBonus: boolean
  awdpAttackScorePerRound?: number | null
  awdpDefenseScorePerRound?: number | null
  awdpMaxAttackAttempts?: number | null
  awdpMaxDefenseAttempts?: number | null
  awdpFixEntry?: string | null
  awdpFixTimeoutSeconds?: number | null
  hints: ChallengeHintDto[]
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
const selectedChallengeId = ref<string | null>(null)
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

const bindForm = reactive({
  templateId: '',
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'sigmoid',
  difficultyCoefficient: 1,
  enableBloodBonus: false,
  flagPrefix: 'flag',
  awdpAttackScorePerRound: 50 as number | undefined,
  awdpDefenseScorePerRound: 100 as number | undefined,
  awdpMaxAttackAttempts: 5 as number | undefined,
  awdpMaxDefenseAttempts: 3 as number | undefined,
  awdpFixEntry: 'fix.sh',
  awdpFixTimeoutSeconds: 60 as number | undefined,
  hints: [''],
})

const selectedEdit = reactive({
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'sigmoid',
  difficultyCoefficient: 1,
  enableBloodBonus: false,
  flagPrefix: 'flag',
  awdpAttackScorePerRound: 50 as number | undefined,
  awdpDefenseScorePerRound: 100 as number | undefined,
  awdpMaxAttackAttempts: 5 as number | undefined,
  awdpMaxDefenseAttempts: 3 as number | undefined,
  awdpFixEntry: 'fix.sh',
  awdpFixTimeoutSeconds: 60 as number | undefined,
  hints: [''],
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

const { data: templates } = useQuery({
  queryKey: queryKeys.adminChallenges,
  queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
})

const { data: competitionChallenges, isLoading: loadingChallenges } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionChallenges(competitionId.value)),
  queryFn: () => adminApi.competitionChallenges<CompetitionChallengeDto[]>(competitionId.value),
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
  bindForm.initialPoints = competitionForm.initialPoints
  bindForm.minimumPoints = competitionForm.minimumPoints
  bindForm.decayFactor = competitionForm.decayFactor
  bindForm.decayFunction = competitionForm.decayFunction
  bindForm.difficultyCoefficient = competitionForm.difficultyCoefficient
  bindForm.flagPrefix = 'flag'
  bindForm.awdpAttackScorePerRound = value.awdpAttackScorePerRound ?? 50
  bindForm.awdpDefenseScorePerRound = value.awdpDefenseScorePerRound ?? 100
  bindForm.awdpMaxAttackAttempts = value.awdpMaxAttackAttempts ?? 5
  bindForm.awdpMaxDefenseAttempts = value.awdpMaxDefenseAttempts ?? 3
  bindForm.awdpFixEntry = value.awdpFixEntry ?? 'fix.sh'
  bindForm.awdpFixTimeoutSeconds = value.awdpFixTimeoutSeconds ?? 60
}, { immediate: true })

watch(templates, (items) => {
  if (!bindForm.templateId && items?.length)
    bindForm.templateId = items[0].id
}, { immediate: true })

const selectedChallenge = computed(() => competitionChallenges.value?.find(c => c.id === selectedChallengeId.value) ?? null)

watch(selectedChallenge, (challenge) => {
  if (!challenge)
    return
  selectedEdit.description = challenge.description ?? ''
  selectedEdit.initialPoints = challenge.pointsConfig?.initialPoints ?? 500
  selectedEdit.minimumPoints = challenge.pointsConfig?.minimumPoints ?? 100
  selectedEdit.decayFactor = challenge.pointsConfig?.decayFactor ?? 450
  selectedEdit.decayFunction = challenge.pointsConfig?.decayFunction ?? 'sigmoid'
  selectedEdit.difficultyCoefficient = challenge.difficultyCoefficient ?? 1
  selectedEdit.enableBloodBonus = challenge.enableBloodBonus ?? false
  selectedEdit.flagPrefix = challenge.flagPrefix ?? 'flag'
  selectedEdit.awdpAttackScorePerRound = challenge.awdpAttackScorePerRound ?? competitionForm.awdpAttackScorePerRound ?? 50
  selectedEdit.awdpDefenseScorePerRound = challenge.awdpDefenseScorePerRound ?? competitionForm.awdpDefenseScorePerRound ?? 100
  selectedEdit.awdpMaxAttackAttempts = challenge.awdpMaxAttackAttempts ?? competitionForm.awdpMaxAttackAttempts ?? 5
  selectedEdit.awdpMaxDefenseAttempts = challenge.awdpMaxDefenseAttempts ?? competitionForm.awdpMaxDefenseAttempts ?? 3
  selectedEdit.awdpFixEntry = challenge.awdpFixEntry ?? competitionForm.awdpFixEntry ?? 'fix.sh'
  selectedEdit.awdpFixTimeoutSeconds = challenge.awdpFixTimeoutSeconds ?? competitionForm.awdpFixTimeoutSeconds ?? 60
  selectedEdit.hints = challenge.hints?.length ? challenge.hints.map(h => h.content) : ['']
}, { immediate: true })

function cleanHints(hints: string[]) {
  return hints.map(h => h.trim()).filter(Boolean)
}

const saveCompetitionMutation = useMutation({
  mutationFn: () => adminApi.updateCompetition(competitionId.value, competitionPayload()),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetition(competitionId.value) })
    toast.success(t('admin.competitionDetail.saveCompetitionSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.saveCompetitionError')),
})

const bindMutation = useMutation({
  mutationFn: () => adminApi.bindCompetitionChallenge(competitionId.value, {
    templateId: bindForm.templateId,
    description: bindForm.description.trim() || undefined,
    descriptionFormat: 'markdown',
    pointsConfig: {
      initialPoints: numberOrDefault(bindForm.initialPoints, 500),
      minimumPoints: numberOrDefault(bindForm.minimumPoints, 100),
      decayFactor: numberOrDefault(bindForm.decayFactor, 450),
      decayFunction: bindForm.decayFunction || 'sigmoid',
    },
    difficultyCoefficient: numberOrDefault(bindForm.difficultyCoefficient, 1),
    enableBloodBonus: bindForm.enableBloodBonus,
    flagPrefix: bindForm.flagPrefix.trim() || 'flag',
    awdpAttackScorePerRound: optionalNumber(bindForm.awdpAttackScorePerRound),
    awdpDefenseScorePerRound: optionalNumber(bindForm.awdpDefenseScorePerRound),
    awdpMaxAttackAttempts: optionalNumber(bindForm.awdpMaxAttackAttempts),
    awdpMaxDefenseAttempts: optionalNumber(bindForm.awdpMaxDefenseAttempts),
    awdpFixEntry: bindForm.awdpFixEntry.trim() || undefined,
    awdpFixTimeoutSeconds: optionalNumber(bindForm.awdpFixTimeoutSeconds),
    hints: cleanHints(bindForm.hints),
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    bindForm.description = ''
    bindForm.hints = ['']
    toast.success(t('admin.competitionDetail.deploySuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.deployError')),
})

const updateChallengeMutation = useMutation({
  mutationFn: () => adminApi.updateCompetitionChallenge(competitionId.value, selectedChallenge.value!.id, {
    description: selectedEdit.description.trim() || undefined,
    descriptionFormat: 'markdown',
    pointsConfig: {
      initialPoints: numberOrDefault(selectedEdit.initialPoints, 500),
      minimumPoints: numberOrDefault(selectedEdit.minimumPoints, 100),
      decayFactor: numberOrDefault(selectedEdit.decayFactor, 450),
      decayFunction: selectedEdit.decayFunction || 'sigmoid',
    },
    difficultyCoefficient: numberOrDefault(selectedEdit.difficultyCoefficient, 1),
    enableBloodBonus: selectedEdit.enableBloodBonus,
    flagPrefix: selectedEdit.flagPrefix.trim() || 'flag',
    awdpAttackScorePerRound: optionalNumber(selectedEdit.awdpAttackScorePerRound),
    awdpDefenseScorePerRound: optionalNumber(selectedEdit.awdpDefenseScorePerRound),
    awdpMaxAttackAttempts: optionalNumber(selectedEdit.awdpMaxAttackAttempts),
    awdpMaxDefenseAttempts: optionalNumber(selectedEdit.awdpMaxDefenseAttempts),
    awdpFixEntry: selectedEdit.awdpFixEntry.trim() || undefined,
    awdpFixTimeoutSeconds: optionalNumber(selectedEdit.awdpFixTimeoutSeconds),
    hints: cleanHints(selectedEdit.hints),
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    toast.success(t('admin.competitionDetail.updateChallengeSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.updateChallengeError')),
})

const deleteChallengeMutation = useMutation({
  mutationFn: (challengeId: string) => adminApi.deleteCompetitionChallenge(competitionId.value, challengeId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    selectedChallengeId.value = null
    toast.success(t('admin.competitionDetail.removeChallengeSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.removeChallengeError')),
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

const restartContainerMutation = useMutation({
  mutationFn: (challengeId: string) => adminApi.restartCompetitionChallengeContainer(competitionId.value, challengeId),
  onSuccess: () => toast.success(t('admin.competitionDetail.containerRestarted')),
  onError: () => toast.error(t('admin.competitionDetail.containerRestartError')),
})

const rebuildScoreboardMutation = useMutation({
  mutationFn: () => adminApi.rebuildScoreboard(competitionId.value) as Promise<{ rows?: number }>,
  onSuccess: (result) => {
    qc.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) })
    toast.success(`Scoreboard rebuilt${result.rows === undefined ? '' : ` (${result.rows} teams)`}`)
  },
  onError: () => toast.error('Unable to rebuild scoreboard'),
})

function isStaticContainer(challenge: CompetitionChallengeDto) {
  return challenge.deploymentType === 'StaticContainer' || challenge.deploymentType === 3
}

function selectChallenge(challenge: CompetitionChallengeDto) {
  selectedChallengeId.value = challenge.id
}

function addBindHint() {
  bindForm.hints.push('')
}

function addEditHint() {
  selectedEdit.hints.push('')
}

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
          Rebuild scoreboard
        </Button>
        <Button variant="outline" size="sm" as-child>
          <RouterLink :to="{ name: 'admin-competition-operations', params: { id: competitionId } }">
            Operations
          </RouterLink>
        </Button>
        <Button v-if="canOpenAwdpScreen" variant="outline" size="sm" as-child>
          <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
            {{ t('awdp.screenEntry') }}
          </RouterLink>
        </Button>
        <Badge v-if="competition?.status" variant="outline" class="capitalize">
          {{ competition.status }}
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
      <TabsList>
        <TabsTrigger
          v-for="section in competitionDetailSections"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
      </TabsList>

      <div
        class="grid gap-6"
        :class="activeSection === 'challenges' ? 'xl:grid-cols-[minmax(0,1fr)_420px]' : ''"
      >
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
            <AdminCompetitionChallengeBinder
              v-if="activeSection === 'challenges'"
              :bind-form="bindForm"
              :templates="templates ?? []"
              :competition-form="competitionForm"
              :deploying="bindMutation.isPending.value"
              @add-hint="addBindHint"
              @deploy="bindMutation.mutate()"
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

        <aside v-if="activeSection === 'challenges'" class="space-y-6">
          <Card class="p-0 overflow-hidden">
            <div class="border-b p-4">
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.competitionChallenges') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.competitionChallengesDescription') }}
              </p>
            </div>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{{ t('admin.challenges.titleColumn') }}</TableHead>
                  <TableHead>{{ t('common.points') }}</TableHead>
                  <TableHead class="w-10" />
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-if="loadingChallenges">
                  <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                    <Loader2 class="mr-2 inline size-4 animate-spin" />
                    {{ t('common.loading') }}
                  </TableCell>
                </TableRow>
                <TableRow v-else-if="!competitionChallenges?.length">
                  <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                    {{ t('admin.competitionDetail.noDeployedChallenges') }}
                  </TableCell>
                </TableRow>
                <TableRow
                  v-for="challenge in competitionChallenges"
                  v-else
                  :key="challenge.id"
                  class="cursor-pointer hover:bg-muted/50"
                  :class="selectedChallengeId === challenge.id ? 'bg-muted/70' : ''"
                  @click="selectChallenge(challenge)"
                >
                  <TableCell>
                    <div class="font-medium">
                      {{ challenge.title }}
                    </div>
                    <div class="text-xs text-muted-foreground">
                      {{ challenge.typeId }}
                    </div>
                  </TableCell>
                  <TableCell class="text-xs">
                    {{ challenge.pointsConfig.minimumPoints }} → {{ challenge.pointsConfig.initialPoints }}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8 text-destructive"
                      @click.stop="deleteChallengeMutation.mutate(challenge.id)"
                    >
                      <Trash2 class="size-4" />
                    </Button>
                    <Button
                      v-if="isStaticContainer(challenge)"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      @click.stop="restartContainerMutation.mutate(challenge.id)"
                    >
                      <Loader2 v-if="restartContainerMutation.isPending.value" class="size-4 animate-spin" />
                      <Save v-else class="size-4" />
                    </Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </Card>

          <Card v-if="selectedChallenge" class="p-4">
            <div class="mb-4">
              <h3 class="font-semibold">
                {{ selectedChallenge.title }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.editChallengeDescription') }}
              </p>
            </div>

            <div class="space-y-4">
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.markdownDescriptionShort') }}</Label>
                <Textarea v-model="selectedEdit.description" class="font-mono text-xs" rows="7" />
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.initial') }}</Label>
                  <Input v-model.number="selectedEdit.initialPoints" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.minimum') }}</Label>
                  <Input v-model.number="selectedEdit.minimumPoints" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.decayFactor') }}</Label>
                  <Input v-model.number="selectedEdit.decayFactor" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.difficulty') }}</Label>
                  <Input v-model.number="selectedEdit.difficultyCoefficient" type="number" min="0.1" step="0.1" />
                </div>
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
                <Select v-model="selectedEdit.decayFunction">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="sigmoid">
                      {{ t('admin.competitionDetail.decaySigmoid') }}
                    </SelectItem>
                    <SelectItem value="quadratic">
                      {{ t('admin.competitionDetail.decayQuadratic') }}
                    </SelectItem>
                    <SelectItem value="logarithmic">
                      {{ t('admin.competitionDetail.decayLogarithmic') }}
                    </SelectItem>
                    <SelectItem value="linear">
                      {{ t('admin.competitionDetail.decayLinear') }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <label v-if="competitionForm.gameModeType === 'Ctf'" class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
                <input v-model="selectedEdit.enableBloodBonus" type="checkbox" class="size-4">
                <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
              </label>
              <DecayCurvePreview :config="selectedEdit" />
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
                <Input v-model="selectedEdit.flagPrefix" placeholder="flag" />
              </div>
              <Card v-if="competitionForm.gameModeType === 'Awdp'" class="p-0">
                <CardContent class="grid gap-3 p-3 sm:grid-cols-2">
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label>
                    <Input v-model.number="selectedEdit.awdpAttackScorePerRound" type="number" min="0" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label>
                    <Input v-model.number="selectedEdit.awdpDefenseScorePerRound" type="number" min="0" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label>
                    <Input v-model.number="selectedEdit.awdpMaxAttackAttempts" type="number" min="1" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label>
                    <Input v-model.number="selectedEdit.awdpMaxDefenseAttempts" type="number" min="1" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label>
                    <Input v-model="selectedEdit.awdpFixEntry" placeholder="fix.sh" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label>
                    <Input v-model.number="selectedEdit.awdpFixTimeoutSeconds" type="number" min="1" />
                  </div>
                </CardContent>
              </Card>
              <div class="space-y-2">
                <div class="flex items-center justify-between">
                  <Label>{{ t('admin.competitionDetail.hints') }}</Label>
                  <Button variant="outline" size="sm" @click="addEditHint">
                    <Plus class="mr-2 size-4" />
                    {{ t('common.add') }}
                  </Button>
                </div>
                <Input v-for="(_, index) in selectedEdit.hints" :key="index" v-model="selectedEdit.hints[index]" :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })" />
              </div>
              <Button class="w-full" :disabled="updateChallengeMutation.isPending.value" @click="updateChallengeMutation.mutate()">
                <Loader2 v-if="updateChallengeMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                {{ t('admin.competitionDetail.saveDeployedChallenge') }}
              </Button>
            </div>
          </Card>
        </aside>
      </div>
    </Tabs>
  </div>
</template>
