<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Check, Loader2, Lock, Plus, Save, ShieldAlert, Trash2, Unlock, X } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import DecayCurvePreview from '@/components/admin/DecayCurvePreview.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
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
    awdpAttackScorePerRound: undefined,
    awdpDefenseScorePerRound: undefined,
    awdpMaxAttackAttempts: undefined,
    awdpMaxDefenseAttempts: undefined,
    awdpAllowAttackAfterBreakSuccess: competitionForm.awdpAllowAttackAfterBreakSuccess,
    awdpAllowDefenseAfterFixSuccess: competitionForm.awdpAllowDefenseAfterFixSuccess,
    awdpServicePenaltyEnabled: competitionForm.awdpServicePenaltyEnabled,
    awdpServicePenaltyPerRound: optionalNumber(competitionForm.awdpServicePenaltyPerRound),
    awdpViolationPenaltyEnabled: competitionForm.awdpViolationPenaltyEnabled,
    awdpViolationPenalty: optionalNumber(competitionForm.awdpViolationPenalty),
    awdpFixEntry: undefined,
    awdpFixTimeoutSeconds: undefined,
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
  <div class="noctf-admin-page">
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

    <nav v-if="!loadingCompetition" class="noctf-toolbar flex-row flex-wrap items-center justify-start gap-2">
      <RouterLink
        v-for="section in competitionDetailSections"
        :key="section.key"
        :to="sectionRoute(section.key)"
        class="rounded-lg px-3 py-2 text-sm font-semibold transition-colors"
        :class="activeSection === section.key ? 'bg-primary text-primary-foreground shadow-sm shadow-primary/20' : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
      >
        {{ t(section.labelKey) }}
      </RouterLink>
    </nav>

    <div v-if="loadingCompetition" class="noctf-state-box text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <div
      v-else
      class="grid gap-6"
      :class="activeSection === 'challenges' ? 'xl:grid-cols-[minmax(0,1fr)_420px]' : ''"
    >
      <section class="space-y-6">
        <div v-if="activeSection === 'settings'" class="noctf-section">
          <div class="mb-5 flex items-center justify-between gap-4">
            <div>
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.settingsTitle') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.settingsDescription') }}
              </p>
            </div>
            <Button :disabled="saveCompetitionMutation.isPending.value" @click="saveCompetitionMutation.mutate()">
              <Save class="mr-2 size-4" />
              {{ t('common.save') }}
            </Button>
          </div>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.titleColumn') }}</Label>
              <Input v-model="competitionForm.title" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.status') }}</Label>
              <Select v-model="competitionForm.status">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Draft">
                    {{ t('competitions.status.draft') }}
                  </SelectItem>
                  <SelectItem value="Published">
                    {{ t('competitions.status.published') }}
                  </SelectItem>
                  <SelectItem value="Running">
                    {{ t('competitions.status.running') }}
                  </SelectItem>
                  <SelectItem value="Paused">
                    {{ t('competitions.status.paused') }}
                  </SelectItem>
                  <SelectItem value="Finished">
                    {{ t('competitions.status.finished') }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitions.description') }}</Label>
              <Textarea v-model="competitionForm.description" rows="3" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.gameMode') }}</Label>
              <Select v-model="competitionForm.gameModeType">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Ctf">
                    CTF
                  </SelectItem>
                  <SelectItem value="Awd">
                    AWD
                  </SelectItem>
                  <SelectItem value="Awdp">
                    AWDP
                  </SelectItem>
                  <SelectItem value="Koh">
                    KoH
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
              <Input v-model.number="competitionForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.maxTeamMembers') }}</Label>
              <Input v-model.number="competitionForm.maxTeamMembers" type="number" min="1" />
            </div>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.teamRegistrationAutoApprove" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.autoApproveTeams') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.tracksEnabled" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.enableTracks') }}</span>
            </label>
            <div v-if="competitionForm.tracksEnabled" class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.trackNames') }}</Label>
              <Textarea v-model="competitionForm.trackNamesText" rows="3" :placeholder="t('admin.competitionDetail.trackNamesPlaceholder')" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.startTime') }}</Label>
              <Input v-model="competitionForm.startTime" type="datetime-local" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.endTime') }}</Label>
              <Input v-model="competitionForm.endTime" type="datetime-local" />
            </div>
          </div>

          <div v-if="competitionForm.gameModeType !== 'Awdp'" class="noctf-fieldset mt-6 grid gap-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.initialPoints') }}</Label>
              <Input v-model.number="competitionForm.initialPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.minimumPoints') }}</Label>
              <Input v-model.number="competitionForm.minimumPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.decayFactor') }}</Label>
              <Input v-model.number="competitionForm.decayFactor" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
              <Select v-model="competitionForm.decayFunction">
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
          </div>

          <div v-if="competitionForm.gameModeType === 'Ctf'" class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-3">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.firstBloodBonus') }}</Label>
              <Input v-model.number="competitionForm.firstBloodBonusPercent" type="number" min="0" step="1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.secondBloodBonus') }}</Label>
              <Input v-model.number="competitionForm.secondBloodBonusPercent" type="number" min="0" step="1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.thirdBloodBonus') }}</Label>
              <Input v-model.number="competitionForm.thirdBloodBonusPercent" type="number" min="0" step="1" />
            </div>
          </div>

          <div v-if="competitionForm.gameModeType === 'Awd' || competitionForm.gameModeType === 'Awdp'" class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.roundDurationSeconds') }}</Label>
              <Input v-model.number="competitionForm.roundDurationSeconds" type="number" min="1" placeholder="300" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.totalRounds') }}</Label>
              <Input v-model.number="competitionForm.totalRounds" type="number" min="1" placeholder="10" />
            </div>
            <template v-if="competitionForm.gameModeType === 'Awd'">
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdAttackPoints') }}</Label>
                <Input v-model.number="competitionForm.attackPoints" type="number" min="0" placeholder="50" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdDefensePoints') }}</Label>
                <Input v-model.number="competitionForm.serviceOnlinePoints" type="number" min="0" placeholder="100" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdServiceDownPenalty') }}</Label>
                <Input v-model.number="competitionForm.serviceDownPenalty" type="number" min="0" placeholder="50" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdBeenAttackedPenalty') }}</Label>
                <Input v-model.number="competitionForm.beenAttackedPenalty" type="number" min="0" placeholder="50" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagValidityRounds') }}</Label>
                <Input v-model.number="competitionForm.flagValidityRounds" type="number" min="1" placeholder="2" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagFormat') }}</Label>
                <Input v-model="competitionForm.flagFormat" placeholder="flag{{{0}}}" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagPath') }}</Label>
                <Input v-model="competitionForm.flagPath" placeholder="/flag/flag.txt" />
              </div>
            </template>
          </div>

          <div v-if="competitionForm.gameModeType === 'Awdp'" class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpServicePenalty') }}</Label>
              <Input v-model.number="competitionForm.awdpServicePenaltyPerRound" type="number" min="0" :disabled="!competitionForm.awdpServicePenaltyEnabled" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpViolationPenalty') }}</Label>
              <Input v-model.number="competitionForm.awdpViolationPenalty" type="number" min="0" :disabled="!competitionForm.awdpViolationPenaltyEnabled" />
            </div>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.awdpAllowAttackAfterBreakSuccess" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.awdpAllowAttackRepeat') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.awdpAllowDefenseAfterFixSuccess" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.awdpAllowDefenseRepeat') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.awdpServicePenaltyEnabled" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.awdpEnableServicePenalty') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.awdpViolationPenaltyEnabled" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.awdpEnableViolationPenalty') }}</span>
            </label>
          </div>
        </div>

        <div v-if="activeSection === 'challenges'" class="noctf-section">
          <div class="mb-5">
            <h3 class="font-semibold">
              {{ t('admin.competitionDetail.deployTitle') }}
            </h3>
            <p class="text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.deployDescription') }}
            </p>
          </div>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.challengeTemplate') }}</Label>
              <Select v-model="bindForm.templateId">
                <SelectTrigger><SelectValue :placeholder="t('admin.competitionDetail.selectTemplate')" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="template in templates" :key="template.id" :value="template.id">
                    {{ template.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
              <Input v-model.number="bindForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
              <Input v-model="bindForm.flagPrefix" placeholder="flag" />
            </div>
            <label v-if="competitionForm.gameModeType === 'Ctf'" class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="bindForm.enableBloodBonus" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
            </label>
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.markdownDescription') }}</Label>
              <Textarea v-model="bindForm.description" class="font-mono text-xs" rows="5" :placeholder="t('admin.competitionDetail.descriptionPlaceholder')" />
            </div>
          </div>

          <div class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.initial') }}</Label>
              <Input v-model.number="bindForm.initialPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.minimum') }}</Label>
              <Input v-model.number="bindForm.minimumPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.decayFactor') }}</Label>
              <Input v-model.number="bindForm.decayFactor" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
              <Select v-model="bindForm.decayFunction">
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
          </div>

          <div v-if="competitionForm.gameModeType === 'Awdp'" class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-3">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label>
              <Input v-model.number="bindForm.awdpAttackScorePerRound" type="number" min="0" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label>
              <Input v-model.number="bindForm.awdpDefenseScorePerRound" type="number" min="0" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label>
              <Input v-model.number="bindForm.awdpFixTimeoutSeconds" type="number" min="1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label>
              <Input v-model.number="bindForm.awdpMaxAttackAttempts" type="number" min="1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label>
              <Input v-model.number="bindForm.awdpMaxDefenseAttempts" type="number" min="1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label>
              <Input v-model="bindForm.awdpFixEntry" placeholder="fix.sh" />
            </div>
          </div>

          <DecayCurvePreview class="mt-4" :config="bindForm" />

          <div class="mt-4 space-y-2">
            <div class="flex items-center justify-between">
              <Label>{{ t('admin.competitionDetail.hints') }}</Label>
              <Button variant="outline" size="sm" @click="addBindHint">
                <Plus class="mr-2 size-4" />
                {{ t('admin.competitionDetail.addHint') }}
              </Button>
            </div>
            <Input v-for="(_, index) in bindForm.hints" :key="index" v-model="bindForm.hints[index]" :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })" />
          </div>

          <div class="mt-5 flex justify-end">
            <Button :disabled="bindMutation.isPending.value || !bindForm.templateId" @click="bindMutation.mutate()">
              <Loader2 v-if="bindMutation.isPending.value" class="mr-2 size-4 animate-spin" />
              {{ t('admin.competitionDetail.deployChallenge') }}
            </Button>
          </div>
        </div>

        <div v-if="activeSection === 'teams'" class="noctf-section">
          <div class="mb-5">
            <h3 class="font-semibold">
              {{ t('admin.competitionDetail.teamReviewTitle') }}
            </h3>
            <p class="text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.teamReviewDescription') }}
            </p>
          </div>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{{ t('admin.teams.name') }}</TableHead>
                <TableHead>{{ t('admin.teams.members') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.track') }}</TableHead>
                <TableHead>{{ t('common.status') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.locked') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.banned') }}</TableHead>
                <TableHead class="text-right">
                  {{ t('common.actions') }}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-if="loadingTeams">
                <TableCell colspan="7" class="h-20 text-center text-muted-foreground">
                  <Loader2 class="mr-2 inline size-4 animate-spin" />
                  {{ t('common.loading') }}
                </TableCell>
              </TableRow>
              <TableRow v-else-if="!competitionTeams?.length">
                <TableCell colspan="7" class="h-20 text-center text-muted-foreground">
                  {{ t('admin.competitionDetail.noTeams') }}
                </TableCell>
              </TableRow>
              <TableRow v-for="team in competitionTeams" v-else :key="team.id">
                <TableCell>
                  <div class="font-medium">
                    {{ team.name }}
                  </div>
                  <div class="text-xs text-muted-foreground">
                    {{ team.captainName }}
                  </div>
                  <code class="mt-1 block text-[10px] text-muted-foreground">{{ team.inviteToken }}</code>
                </TableCell>
                <TableCell>{{ team.memberCount }} / {{ competitionForm.maxTeamMembers }}</TableCell>
                <TableCell class="text-xs text-muted-foreground">
                  {{ team.trackName || '-' }}
                </TableCell>
                <TableCell>
                  <Badge :variant="team.registrationStatus === 'approved' ? 'default' : team.registrationStatus === 'rejected' ? 'destructive' : 'secondary'">
                    {{ team.registrationStatus }}
                  </Badge>
                </TableCell>
                <TableCell>
                  <Badge :variant="team.isLocked ? 'outline' : 'secondary'">
                    {{ team.isLocked ? t('admin.competitionDetail.locked') : t('admin.competitionDetail.unlocked') }}
                  </Badge>
                </TableCell>
                <TableCell>
                  <Badge :variant="team.isBanned ? 'destructive' : 'secondary'">
                    {{ team.isBanned ? t('admin.competitionDetail.banned') : t('admin.competitionDetail.normal') }}
                  </Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-1">
                    <Button v-if="team.registrationStatus !== 'approved'" variant="ghost" size="icon" class="size-8" @click="approveTeamMutation.mutate(team.id)">
                      <Check class="size-4" />
                    </Button>
                    <Button v-if="team.registrationStatus !== 'rejected'" variant="ghost" size="icon" class="size-8 text-destructive" @click="rejectTeamMutation.mutate(team.id)">
                      <X class="size-4" />
                    </Button>
                    <Button variant="ghost" size="icon" class="size-8" @click="lockTeamMutation.mutate({ teamId: team.id, isLocked: !team.isLocked })">
                      <Unlock v-if="team.isLocked" class="size-4" />
                      <Lock v-else class="size-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :class="team.isBanned ? '' : 'text-destructive'"
                      @click="team.isBanned ? unbanTeamMutation.mutate(team.id) : banTeamMutation.mutate(team.id)"
                    >
                      <ShieldAlert class="size-4" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div v-if="activeSection === 'cheats'" class="noctf-section">
          <div class="mb-5">
            <h3 class="font-semibold">
              {{ t('admin.competitionDetail.cheatTitle') }}
            </h3>
            <p class="text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.cheatDescription') }}
            </p>
          </div>
          <div v-if="loadingCheatIncidents" class="py-8 text-center text-sm text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />
            {{ t('common.loading') }}
          </div>
          <div v-else-if="!cheatIncidents?.length" class="noctf-state-box min-h-0 p-6 text-sm text-muted-foreground">
            {{ t('admin.competitionDetail.noCheatIncidents') }}
          </div>
          <div v-else class="space-y-3">
            <div v-for="incident in cheatIncidents" :key="incident.id" class="rounded-lg border bg-muted/30 p-3">
              <div class="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <div class="font-medium">
                    {{ incident.suspectTeamName }} → {{ incident.victimTeamName || '-' }}
                  </div>
                  <div class="text-xs text-muted-foreground">
                    {{ incident.challengeTitle }} / {{ incident.userName }} / {{ new Date(incident.createdAt).toLocaleString() }}
                  </div>
                </div>
                <Button size="sm" variant="destructive" @click="banTeamMutation.mutate(incident.suspectTeamId)">
                  <ShieldAlert class="mr-2 size-4" />
                  {{ t('admin.competitionDetail.banTeam') }}
                </Button>
              </div>
            </div>
          </div>
        </div>

        <div v-if="activeSection === 'logs'" class="noctf-section">
          <div class="mb-5">
            <h3 class="font-semibold">
              {{ t('admin.competitionDetail.logsTitle') }}
            </h3>
            <p class="text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.logsDescription') }}
            </p>
          </div>
          <div v-if="loadingLogs" class="py-8 text-center text-sm text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />
            {{ t('common.loading') }}
          </div>
          <div v-else class="noctf-scrollbar max-h-[360px] space-y-2 overflow-y-auto pr-1">
            <div v-for="log in competitionLogs ?? []" :key="log.id" class="rounded-lg border bg-muted/30 p-3 text-sm">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <Badge :variant="log.level === 'error' ? 'destructive' : log.level === 'warning' ? 'secondary' : 'outline'">
                  {{ log.eventType }}
                </Badge>
                <span class="text-xs text-muted-foreground">{{ new Date(log.createdAt).toLocaleString() }}</span>
              </div>
              <p class="mt-2">
                {{ log.message }}
              </p>
              <p v-if="log.teamName || log.challengeTitle" class="mt-1 text-xs text-muted-foreground">
                {{ log.teamName || '-' }} / {{ log.challengeTitle || '-' }}
              </p>
            </div>
            <div v-if="!competitionLogs?.length" class="noctf-state-box min-h-0 p-6 text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.noLogs') }}
            </div>
          </div>
        </div>
      </section>

      <aside v-if="activeSection === 'challenges'" class="space-y-6">
        <div class="noctf-surface">
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
        </div>

        <div v-if="selectedChallenge" class="noctf-section">
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
            <div v-if="competitionForm.gameModeType === 'Awdp'" class="grid gap-3 rounded-lg border bg-muted/30 p-3 sm:grid-cols-2">
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
            </div>
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
        </div>
      </aside>
    </div>
  </div>
</template>
