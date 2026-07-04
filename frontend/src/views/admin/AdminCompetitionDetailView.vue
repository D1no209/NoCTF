<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  ArrowLeft,
  Check,
  Loader2,
  Lock,
  Plus,
  RefreshCw,
  RotateCw,
  Save,
  ShieldAlert,
  Trash2,
  Unlock,
  X,
} from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import DecayCurvePreview from '@/components/admin/DecayCurvePreview.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
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
  orchestrationJson?: string
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
  orchestrationJson?: string
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
  victimTeamId?: string | null
  victimTeamName?: string | null
  challengeTitle: string
  userName: string
  submittedFlag?: string | null
  reason: string
  resolved: boolean
  createdAt: string
}

interface PenetrationAdminInstanceDto {
  id: string
  teamId: string
  teamName: string
  challengeId: string
  challengeTitle: string
  status: string
  entryUrl?: string | null
  resetCount: number
  expiresAt?: string | null
  lastError?: string | null
  createdAt: string
  updatedAt: string
}

interface PenetrationAdminInstanceListDto {
  items: PenetrationAdminInstanceDto[]
}

const route = useRoute()
const router = useRouter()
const qc = useQueryClient()
const { t } = useI18n()
const competitionId = computed(() => String(route.params.id))
const selectedChallengeId = ref<string | null>(null)
const competitionDetailSections = [
  { key: 'overview', labelKey: 'admin.competitionDetail.navOverview' },
  { key: 'settings', labelKey: 'admin.competitionDetail.navSettings' },
  { key: 'challenges', labelKey: 'admin.competitionDetail.navChallenges' },
  { key: 'teams', labelKey: 'admin.competitionDetail.navTeams' },
  { key: 'instances', labelKey: 'admin.competitionDetail.navInstances' },
  { key: 'logs', labelKey: 'admin.competitionDetail.navLogs' },
] as const
type CompetitionDetailSection = (typeof competitionDetailSections)[number]['key']
const legacyCompetitionSectionMap: Record<string, CompetitionDetailSection> = {
  configure: 'settings',
  operate: 'teams',
  audit: 'logs',
  cheats: 'logs',
}
const trackNamesSeparatorPattern = /\r?\n|,/
interface DangerAction {
  title: string
  description: string
  confirmLabel: string
  run: () => void
}
const dangerDialogOpen = ref(false)
const dangerConfirmText = ref('')
const pendingDangerAction = ref<DangerAction | null>(null)

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
  orchestrationJson: '{}',
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
  orchestrationJson: '{}',
  hints: [''],
})
const selectedPenetrationTopologyJson = ref('')
const selectedPenetrationTopologyError = ref('')
const bindOrchestrationError = ref('')
const editOrchestrationError = ref('')
const loadingSelectedPenetrationTopology = ref(false)
const penetrationInstanceFilters = reactive({
  challengeId: 'all',
  teamId: 'all',
})
let selectedPenetrationTopologyLoadId = 0

function toDateTimeLocal(value: string) {
  if (!value) return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function optionalNumber(value: number | string | undefined | null) {
  if (value === undefined || value === null || value === '') return undefined
  return Number(value)
}

function numberOrDefault(value: number | string | undefined | null, fallback: number) {
  if (value === undefined || value === null || value === '') return fallback
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
    trackNames: competitionForm.trackNamesText
      .split(trackNamesSeparatorPattern)
      .map((item) => item.trim())
      .filter(Boolean),
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

const penetrationInstanceQuery = computed(() => {
  const query: Record<string, string> = {}
  if (penetrationInstanceFilters.challengeId !== 'all')
    query.challengeId = penetrationInstanceFilters.challengeId
  if (penetrationInstanceFilters.teamId !== 'all') query.teamId = penetrationInstanceFilters.teamId
  return query
})

const {
  data: penetrationInstancesResponse,
  isLoading: loadingPenetrationInstances,
  refetch: refetchPenetrationInstances,
} = useQuery({
  queryKey: computed(() =>
    queryKeys.adminPenetrationInstances(competitionId.value, penetrationInstanceQuery.value),
  ),
  queryFn: () =>
    adminApi.penetrationInstances<PenetrationAdminInstanceListDto>(
      competitionId.value,
      penetrationInstanceQuery.value,
    ),
})

const penetrationInstances = computed(() => penetrationInstancesResponse.value?.items ?? [])
const penetrationChallenges = computed(() =>
  (competitionChallenges.value ?? []).filter(
    (challenge) => challenge.typeId?.toLowerCase() === 'penetration',
  ),
)
const isCtfMode = computed(
  () => (competition.value?.gameModeType ?? competitionForm.gameModeType).toLowerCase() === 'ctf',
)
const showInstanceOperations = computed(
  () =>
    !isCtfMode.value ||
    penetrationChallenges.value.length > 0 ||
    penetrationInstances.value.length > 0,
)
const deployedChallengeCount = computed(() => competitionChallenges.value?.length ?? 0)
const teamCount = computed(() => competitionTeams.value?.length ?? 0)
const pendingTeamCount = computed(
  () =>
    (competitionTeams.value ?? []).filter((team) => team.registrationStatus !== 'approved').length,
)
const activeInstanceCount = computed(
  () =>
    penetrationInstances.value.filter((instance) => instance.status.toLowerCase() === 'running')
      .length,
)
const unresolvedCheatCount = computed(
  () => (cheatIncidents.value ?? []).filter((incident) => !incident.resolved).length,
)
const latestCompetitionLog = computed(() => competitionLogs.value?.[0] ?? null)

watch(
  competition,
  (value) => {
    if (!value) return
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
    competitionForm.awdpAllowAttackAfterBreakSuccess =
      value.awdpAllowAttackAfterBreakSuccess ?? false
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
  },
  { immediate: true },
)

watch(
  templates,
  (items) => {
    if (!bindForm.templateId && items?.length) bindForm.templateId = items[0].id
  },
  { immediate: true },
)

const selectedTemplate = computed(
  () => templates.value?.find((template) => template.id === bindForm.templateId) ?? null,
)

watch(
  () => bindForm.templateId,
  () => {
    bindOrchestrationError.value = ''
    bindForm.orchestrationJson = selectedTemplate.value?.orchestrationJson || '{}'
  },
)

const selectedChallenge = computed(
  () => competitionChallenges.value?.find((c) => c.id === selectedChallengeId.value) ?? null,
)
const selectedIsPenetration = computed(
  () => selectedChallenge.value?.typeId?.toLowerCase() === 'penetration',
)

async function loadSelectedPenetrationTopology(challengeId: string) {
  const loadId = ++selectedPenetrationTopologyLoadId
  selectedPenetrationTopologyError.value = ''
  loadingSelectedPenetrationTopology.value = true
  try {
    const topology = await adminApi.competitionPenetrationTopology<Record<string, unknown>>(
      competitionId.value,
      challengeId,
    )
    if (loadId !== selectedPenetrationTopologyLoadId) return
    selectedPenetrationTopologyJson.value = JSON.stringify(topology, null, 2)
  } catch {
    if (loadId !== selectedPenetrationTopologyLoadId) return
    selectedPenetrationTopologyJson.value = ''
    selectedPenetrationTopologyError.value = t('errors.loadFailed')
  } finally {
    if (loadId === selectedPenetrationTopologyLoadId)
      loadingSelectedPenetrationTopology.value = false
  }
}

watch(
  selectedChallenge,
  (challenge) => {
    if (!challenge) {
      selectedPenetrationTopologyJson.value = ''
      selectedPenetrationTopologyError.value = ''
      return
    }
    selectedEdit.description = challenge.description ?? ''
    selectedEdit.initialPoints = challenge.pointsConfig?.initialPoints ?? 500
    selectedEdit.minimumPoints = challenge.pointsConfig?.minimumPoints ?? 100
    selectedEdit.decayFactor = challenge.pointsConfig?.decayFactor ?? 450
    selectedEdit.decayFunction = challenge.pointsConfig?.decayFunction ?? 'sigmoid'
    selectedEdit.difficultyCoefficient = challenge.difficultyCoefficient ?? 1
    selectedEdit.enableBloodBonus = challenge.enableBloodBonus ?? false
    selectedEdit.flagPrefix = challenge.flagPrefix ?? 'flag'
    selectedEdit.awdpAttackScorePerRound =
      challenge.awdpAttackScorePerRound ?? competitionForm.awdpAttackScorePerRound ?? 50
    selectedEdit.awdpDefenseScorePerRound =
      challenge.awdpDefenseScorePerRound ?? competitionForm.awdpDefenseScorePerRound ?? 100
    selectedEdit.awdpMaxAttackAttempts =
      challenge.awdpMaxAttackAttempts ?? competitionForm.awdpMaxAttackAttempts ?? 5
    selectedEdit.awdpMaxDefenseAttempts =
      challenge.awdpMaxDefenseAttempts ?? competitionForm.awdpMaxDefenseAttempts ?? 3
    selectedEdit.awdpFixEntry = challenge.awdpFixEntry ?? competitionForm.awdpFixEntry ?? 'fix.sh'
    selectedEdit.awdpFixTimeoutSeconds =
      challenge.awdpFixTimeoutSeconds ?? competitionForm.awdpFixTimeoutSeconds ?? 60
    selectedEdit.orchestrationJson = challenge.orchestrationJson || '{}'
    selectedEdit.hints = challenge.hints?.length ? challenge.hints.map((h) => h.content) : ['']
    editOrchestrationError.value = ''
    selectedPenetrationTopologyJson.value = ''
    selectedPenetrationTopologyError.value = ''
    if (challenge.typeId?.toLowerCase() === 'penetration')
      void loadSelectedPenetrationTopology(challenge.id)
  },
  { immediate: true },
)

function cleanHints(hints: string[]) {
  return hints.map((h) => h.trim()).filter(Boolean)
}

function parseJsonObject(value: string) {
  const parsed = JSON.parse(value || '{}')
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object')
    throw new Error('invalid_json_object')
  return parsed as Record<string, unknown>
}

function optionalOrchestrationJson(value: string, error: typeof bindOrchestrationError) {
  error.value = ''
  const text = value.trim()
  if (!text) return undefined
  try {
    parseJsonObject(text)
    return text
  } catch {
    error.value = t('admin.challenges.invalidOrchestration')
    throw new Error('invalid_orchestration_json')
  }
}

function isHttpUrl(value?: string | null) {
  return Boolean(value && /^https?:\/\//i.test(value))
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
  mutationFn: () => {
    const orchestrationJson = optionalOrchestrationJson(
      bindForm.orchestrationJson,
      bindOrchestrationError,
    )
    return adminApi.bindCompetitionChallenge(competitionId.value, {
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
      orchestrationJson,
      hints: cleanHints(bindForm.hints),
    })
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    bindForm.description = ''
    bindForm.orchestrationJson = '{}'
    bindForm.hints = ['']
    toast.success(t('admin.competitionDetail.deploySuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.deployError')),
})

const updateChallengeMutation = useMutation({
  mutationFn: () => {
    const orchestrationJson = optionalOrchestrationJson(
      selectedEdit.orchestrationJson,
      editOrchestrationError,
    )
    return adminApi.updateCompetitionChallenge(competitionId.value, selectedChallenge.value!.id, {
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
      orchestrationJson,
      hints: cleanHints(selectedEdit.hints),
    })
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    toast.success(t('admin.competitionDetail.updateChallengeSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.updateChallengeError')),
})

const updatePenetrationTopologyMutation = useMutation({
  mutationFn: async () => {
    if (!selectedChallenge.value) return
    selectedPenetrationTopologyError.value = ''
    let payload: Record<string, unknown>
    try {
      payload = JSON.parse(selectedPenetrationTopologyJson.value) as Record<string, unknown>
    } catch {
      selectedPenetrationTopologyError.value = t('penetration.invalidTopology')
      throw new Error('invalid_topology_json')
    }
    await adminApi.updateCompetitionPenetrationTopology(
      competitionId.value,
      selectedChallenge.value.id,
      payload,
    )
  },
  onSuccess: () => {
    if (selectedChallenge.value) void loadSelectedPenetrationTopology(selectedChallenge.value.id)
    toast.success(t('admin.competitionDetail.updateChallengeSuccess'))
  },
  onError: (error) => {
    if (error instanceof Error && error.message === 'invalid_topology_json') return
    toast.error(t('admin.competitionDetail.updateChallengeError'))
  },
})

const deleteChallengeMutation = useMutation({
  mutationFn: (challengeId: string) =>
    adminApi.deleteCompetitionChallenge(competitionId.value, challengeId),
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
  mutationFn: ({ teamId, isLocked }: { teamId: string; isLocked: boolean }) =>
    adminApi.setCompetitionTeamLock(competitionId.value, teamId, isLocked),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamLockUpdated'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const banTeamMutation = useMutation({
  mutationFn: (teamId: string) =>
    adminApi.banCompetitionTeam(competitionId.value, teamId, 'suspected_cheat'),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamBanned'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const banTeamsMutation = useMutation({
  mutationFn: async (teamIds: string[]) => {
    await Promise.all(
      [...new Set(teamIds)].map((teamId) =>
        adminApi.banCompetitionTeam(competitionId.value, teamId, 'suspected_cheat'),
      ),
    )
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.teamsBanned'))
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
  mutationFn: (challengeId: string) =>
    adminApi.restartCompetitionChallengeContainer(competitionId.value, challengeId),
  onSuccess: () => toast.success(t('admin.competitionDetail.containerRestarted')),
  onError: () => toast.error(t('admin.competitionDetail.containerRestartError')),
})

const resetPenetrationInstanceMutation = useMutation({
  mutationFn: (instanceId: string) =>
    adminApi.resetPenetrationInstance(competitionId.value, instanceId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['admin-penetration-instances', competitionId.value] })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.penetrationInstanceResetSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.penetrationInstanceActionError')),
})

const destroyPenetrationInstanceMutation = useMutation({
  mutationFn: (instanceId: string) =>
    adminApi.destroyPenetrationInstance(competitionId.value, instanceId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['admin-penetration-instances', competitionId.value] })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    toast.success(t('admin.competitionDetail.penetrationInstanceDestroyed'))
  },
  onError: () => toast.error(t('admin.competitionDetail.penetrationInstanceActionError')),
})

const dangerConfirmed = computed(
  () => dangerConfirmText.value === (pendingDangerAction.value?.confirmLabel ?? ''),
)

function openDangerAction(action: DangerAction) {
  pendingDangerAction.value = action
  dangerConfirmText.value = ''
  dangerDialogOpen.value = true
}

function confirmDangerAction() {
  if (!pendingDangerAction.value || !dangerConfirmed.value) return

  pendingDangerAction.value.run()
  dangerDialogOpen.value = false
  dangerConfirmText.value = ''
  pendingDangerAction.value = null
}

function openDeleteChallenge(challenge: CompetitionChallengeDto) {
  openDangerAction({
    title: t('admin.competitionDetail.deleteChallengeTitle'),
    description: t('admin.competitionDetail.deleteChallengeDescription', {
      title: challenge.title,
    }),
    confirmLabel: challenge.title,
    run: () => deleteChallengeMutation.mutate(challenge.id),
  })
}

function openBanTeam(teamId: string, teamName: string) {
  openDangerAction({
    title: t('admin.competitionDetail.banTeamTitle'),
    description: t('admin.competitionDetail.banTeamDescription', { name: teamName }),
    confirmLabel: teamName,
    run: () => banTeamMutation.mutate(teamId),
  })
}

function incidentVictimTeamId(incident: CheatIncidentDto) {
  if (incident.victimTeamId) return incident.victimTeamId
  if (!incident.victimTeamName) return null

  return competitionTeams.value?.find((team) => team.name === incident.victimTeamName)?.id ?? null
}

function incidentVictimTeamName(incident: CheatIncidentDto) {
  return incident.victimTeamName || t('admin.competitionDetail.unknownTeam')
}

function canBanVictimTeam(incident: CheatIncidentDto) {
  const victimTeamId = incidentVictimTeamId(incident)
  return Boolean(victimTeamId && victimTeamId !== incident.suspectTeamId)
}

function openBanVictimTeam(incident: CheatIncidentDto) {
  const victimTeamId = incidentVictimTeamId(incident)
  if (!victimTeamId) return

  openBanTeam(victimTeamId, incidentVictimTeamName(incident))
}

function openBanBothTeams(incident: CheatIncidentDto) {
  const victimTeamId = incidentVictimTeamId(incident)
  if (!victimTeamId || victimTeamId === incident.suspectTeamId) return

  const teamNames = `${incident.suspectTeamName} + ${incidentVictimTeamName(incident)}`
  openDangerAction({
    title: t('admin.competitionDetail.banBothTeamsTitle'),
    description: t('admin.competitionDetail.banBothTeamsDescription', {
      suspect: incident.suspectTeamName,
      victim: incidentVictimTeamName(incident),
    }),
    confirmLabel: teamNames,
    run: () => banTeamsMutation.mutate([incident.suspectTeamId, victimTeamId]),
  })
}

function openDestroyInstance(instance: PenetrationAdminInstanceDto) {
  openDangerAction({
    title: t('admin.competitionDetail.destroyInstanceTitle'),
    description: t('admin.competitionDetail.destroyInstanceDescription', {
      team: instance.teamName,
      challenge: instance.challengeTitle,
    }),
    confirmLabel: instance.id,
    run: () => destroyPenetrationInstanceMutation.mutate(instance.id),
  })
}

function isStaticContainer(challenge: CompetitionChallengeDto) {
  return challenge.deploymentType === 'StaticContainer' || challenge.deploymentType === 3
}

function penetrationStatusVariant(status: string) {
  const normalized = status.toLowerCase()
  if (normalized === 'running') return 'default'
  if (normalized === 'failed' || normalized === 'expired') return 'destructive'
  if (normalized === 'stopped' || normalized === 'destroyed') return 'secondary'
  return 'outline'
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleString() : '-'
}

function selectChallenge(challenge: CompetitionChallengeDto) {
  selectedChallengeId.value = challenge.id
}

function clearSelectedChallenge() {
  selectedChallengeId.value = null
}

function addBindHint() {
  bindForm.hints.push('')
}

function addEditHint() {
  selectedEdit.hints.push('')
}

const activeSection = computed<CompetitionDetailSection>(() => {
  const section = route.query.section
  if (typeof section === 'string' && competitionDetailSections.some((item) => item.key === section))
    return section as CompetitionDetailSection
  if (typeof section === 'string' && legacyCompetitionSectionMap[section])
    return legacyCompetitionSectionMap[section]

  return 'overview'
})

const canOpenAwdpScreen = computed(
  () => (competition.value?.gameModeType ?? competitionForm.gameModeType).toLowerCase() === 'awdp',
)

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
        <Button
          variant="ghost"
          size="sm"
          class="-ml-2"
          @click="router.push({ name: 'admin-competitions' })"
        >
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitions.title') }}
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">
            {{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}
          </h2>
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

    <nav
      v-if="!loadingCompetition"
      class="noctf-top-tabs"
      :aria-label="t('admin.competitionDetail.detailNavigation')"
    >
      <RouterLink
        v-for="section in competitionDetailSections"
        :key="section.key"
        :to="sectionRoute(section.key)"
        class="noctf-top-tab"
        :class="
          activeSection === section.key
            ? 'noctf-top-tab-active'
            : 'text-muted-foreground hover:text-foreground'
        "
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
      <section class="min-w-0 space-y-6">
        <div v-if="activeSection === 'overview'" class="space-y-6">
          <div class="noctf-status-strip grid-cols-1 md:grid-cols-2 xl:grid-cols-5">
            <div class="noctf-status-item border-b md:border-r xl:border-b-0">
              <div class="min-w-0">
                <p class="noctf-label">{{ t('admin.competitionDetail.overviewStatus') }}</p>
                <p class="mt-1 text-lg font-semibold capitalize">
                  {{ competition?.status ?? '-' }}
                </p>
              </div>
            </div>
            <div class="noctf-status-item border-b xl:border-r xl:border-b-0">
              <div class="min-w-0">
                <p class="noctf-label">{{ t('admin.competitionDetail.overviewChallenges') }}</p>
                <p class="mt-1 text-lg font-semibold">{{ deployedChallengeCount }}</p>
              </div>
            </div>
            <div class="noctf-status-item border-b md:border-r xl:border-b-0">
              <div class="min-w-0">
                <p class="noctf-label">{{ t('admin.competitionDetail.overviewTeams') }}</p>
                <p class="mt-1 text-lg font-semibold">{{ teamCount }}</p>
                <p v-if="pendingTeamCount" class="mt-1 text-xs text-muted-foreground">
                  {{ pendingTeamCount }} {{ t('admin.competitionDetail.overviewPending') }}
                </p>
              </div>
            </div>
            <div class="noctf-status-item border-b xl:border-r xl:border-b-0">
              <div class="min-w-0">
                <p class="noctf-label">
                  {{ t('admin.competitionDetail.overviewActiveInstances') }}
                </p>
                <p class="mt-1 text-lg font-semibold">{{ activeInstanceCount }}</p>
              </div>
            </div>
            <div class="noctf-status-item">
              <div class="min-w-0">
                <p class="noctf-label">{{ t('admin.competitionDetail.overviewSignals') }}</p>
                <p
                  class="mt-1 text-lg font-semibold"
                  :class="unresolvedCheatCount ? 'text-destructive' : ''"
                >
                  {{ unresolvedCheatCount }}
                </p>
              </div>
            </div>
          </div>

          <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
            <div class="noctf-workbench">
              <div class="border-b border-border/80 p-4">
                <h3 class="font-semibold">{{ t('admin.competitionDetail.overviewTitle') }}</h3>
                <p class="text-sm text-muted-foreground">
                  {{ t('admin.competitionDetail.overviewDescription') }}
                </p>
              </div>
              <div class="grid gap-0 md:grid-cols-2">
                <div class="space-y-3 p-4">
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{
                      t('admin.competitions.gameMode')
                    }}</span>
                    <span class="font-medium">{{ competition?.gameModeType ?? '-' }}</span>
                  </div>
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{
                      t('admin.competitions.startTime')
                    }}</span>
                    <span class="font-medium">{{ formatDate(competition?.startTime) }}</span>
                  </div>
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{ t('admin.competitions.endTime') }}</span>
                    <span class="font-medium">{{ formatDate(competition?.endTime) }}</span>
                  </div>
                </div>
                <div class="space-y-3 border-t border-border/80 p-4 md:border-l md:border-t-0">
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{
                      t('admin.competitionDetail.minimumPoints')
                    }}</span>
                    <span class="font-medium">{{ competitionForm.minimumPoints }}</span>
                  </div>
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{
                      t('admin.competitionDetail.initialPoints')
                    }}</span>
                    <span class="font-medium">{{ competitionForm.initialPoints }}</span>
                  </div>
                  <div class="grid grid-cols-[9rem_minmax(0,1fr)] gap-3 text-sm">
                    <span class="text-muted-foreground">{{
                      t('admin.competitionDetail.difficultyCoefficient')
                    }}</span>
                    <span class="font-medium">{{ competitionForm.difficultyCoefficient }}</span>
                  </div>
                </div>
              </div>
            </div>

            <div class="noctf-workbench">
              <div class="border-b border-border/80 p-4">
                <h3 class="font-semibold">{{ t('admin.competitionDetail.nextActions') }}</h3>
              </div>
              <div class="grid gap-2 p-4">
                <Button variant="outline" class="justify-start" as-child>
                  <RouterLink :to="sectionRoute('settings')">{{
                    t('admin.competitionDetail.navSettings')
                  }}</RouterLink>
                </Button>
                <Button variant="outline" class="justify-start" as-child>
                  <RouterLink :to="sectionRoute('challenges')">{{
                    t('admin.competitionDetail.navChallenges')
                  }}</RouterLink>
                </Button>
                <Button variant="outline" class="justify-start" as-child>
                  <RouterLink :to="sectionRoute('teams')">{{
                    t('admin.competitionDetail.navTeams')
                  }}</RouterLink>
                </Button>
                <Button variant="outline" class="justify-start" as-child>
                  <RouterLink :to="sectionRoute('instances')">{{
                    t('admin.competitionDetail.navInstances')
                  }}</RouterLink>
                </Button>
                <Button variant="outline" class="justify-start" as-child>
                  <RouterLink :to="sectionRoute('logs')">{{
                    t('admin.competitionDetail.navLogs')
                  }}</RouterLink>
                </Button>
              </div>
              <div class="border-t border-border/80 p-4">
                <p class="noctf-label">{{ t('admin.competitionDetail.latestEvent') }}</p>
                <p class="mt-2 text-sm text-muted-foreground">
                  {{ latestCompetitionLog?.message ?? t('admin.competitionDetail.noLogs') }}
                </p>
              </div>
            </div>
          </div>
        </div>

        <div v-if="activeSection === 'settings'" class="noctf-workbench p-5">
          <div class="mb-5 flex items-center justify-between gap-4">
            <div>
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.settingsTitle') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.settingsDescription') }}
              </p>
            </div>
            <Button
              :disabled="saveCompetitionMutation.isPending.value"
              @click="saveCompetitionMutation.mutate()"
            >
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
                  <SelectItem value="Ctf"> CTF </SelectItem>
                  <SelectItem value="Awd"> AWD </SelectItem>
                  <SelectItem value="Awdp"> AWDP </SelectItem>
                  <SelectItem value="Koh"> KoH </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
              <Input
                v-model.number="competitionForm.difficultyCoefficient"
                type="number"
                min="0.1"
                step="0.1"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.maxTeamMembers') }}</Label>
              <Input v-model.number="competitionForm.maxTeamMembers" type="number" min="1" />
            </div>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input
                v-model="competitionForm.teamRegistrationAutoApprove"
                type="checkbox"
                class="size-4"
              />
              <span>{{ t('admin.competitionDetail.autoApproveTeams') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input v-model="competitionForm.tracksEnabled" type="checkbox" class="size-4" />
              <span>{{ t('admin.competitionDetail.enableTracks') }}</span>
            </label>
            <div v-if="competitionForm.tracksEnabled" class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.trackNames') }}</Label>
              <Textarea
                v-model="competitionForm.trackNamesText"
                rows="3"
                :placeholder="t('admin.competitionDetail.trackNamesPlaceholder')"
              />
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

          <div
            v-if="competitionForm.gameModeType !== 'Awdp'"
            class="noctf-fieldset mt-6 grid gap-4 lg:grid-cols-4"
          >
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

          <div
            v-if="competitionForm.gameModeType === 'Ctf'"
            class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-3"
          >
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.firstBloodBonus') }}</Label>
              <Input
                v-model.number="competitionForm.firstBloodBonusPercent"
                type="number"
                min="0"
                step="1"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.secondBloodBonus') }}</Label>
              <Input
                v-model.number="competitionForm.secondBloodBonusPercent"
                type="number"
                min="0"
                step="1"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.thirdBloodBonus') }}</Label>
              <Input
                v-model.number="competitionForm.thirdBloodBonusPercent"
                type="number"
                min="0"
                step="1"
              />
            </div>
          </div>

          <div
            v-if="competitionForm.gameModeType === 'Awd' || competitionForm.gameModeType === 'Awdp'"
            class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4"
          >
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.roundDurationSeconds') }}</Label>
              <Input
                v-model.number="competitionForm.roundDurationSeconds"
                type="number"
                min="1"
                placeholder="300"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.totalRounds') }}</Label>
              <Input
                v-model.number="competitionForm.totalRounds"
                type="number"
                min="1"
                placeholder="10"
              />
            </div>
            <template v-if="competitionForm.gameModeType === 'Awd'">
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdAttackPoints') }}</Label>
                <Input
                  v-model.number="competitionForm.attackPoints"
                  type="number"
                  min="0"
                  placeholder="50"
                />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdDefensePoints') }}</Label>
                <Input
                  v-model.number="competitionForm.serviceOnlinePoints"
                  type="number"
                  min="0"
                  placeholder="100"
                />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdServiceDownPenalty') }}</Label>
                <Input
                  v-model.number="competitionForm.serviceDownPenalty"
                  type="number"
                  min="0"
                  placeholder="50"
                />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.awdBeenAttackedPenalty') }}</Label>
                <Input
                  v-model.number="competitionForm.beenAttackedPenalty"
                  type="number"
                  min="0"
                  placeholder="50"
                />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagValidityRounds') }}</Label>
                <Input
                  v-model.number="competitionForm.flagValidityRounds"
                  type="number"
                  min="1"
                  placeholder="2"
                />
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

          <div
            v-if="competitionForm.gameModeType === 'Awdp'"
            class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4"
          >
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpServicePenalty') }}</Label>
              <Input
                v-model.number="competitionForm.awdpServicePenaltyPerRound"
                type="number"
                min="0"
                :disabled="!competitionForm.awdpServicePenaltyEnabled"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpViolationPenalty') }}</Label>
              <Input
                v-model.number="competitionForm.awdpViolationPenalty"
                type="number"
                min="0"
                :disabled="!competitionForm.awdpViolationPenaltyEnabled"
              />
            </div>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input
                v-model="competitionForm.awdpAllowAttackAfterBreakSuccess"
                type="checkbox"
                class="size-4"
              />
              <span>{{ t('admin.competitionDetail.awdpAllowAttackRepeat') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input
                v-model="competitionForm.awdpAllowDefenseAfterFixSuccess"
                type="checkbox"
                class="size-4"
              />
              <span>{{ t('admin.competitionDetail.awdpAllowDefenseRepeat') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input
                v-model="competitionForm.awdpServicePenaltyEnabled"
                type="checkbox"
                class="size-4"
              />
              <span>{{ t('admin.competitionDetail.awdpEnableServicePenalty') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
              <input
                v-model="competitionForm.awdpViolationPenaltyEnabled"
                type="checkbox"
                class="size-4"
              />
              <span>{{ t('admin.competitionDetail.awdpEnableViolationPenalty') }}</span>
            </label>
          </div>
        </div>

        <div v-if="activeSection === 'challenges' && selectedChallenge" class="noctf-workbench p-5">
          <div class="mb-5 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-2">
                <h3 class="truncate font-semibold">
                  {{ selectedChallenge.title }}
                </h3>
                <Badge variant="outline">{{ selectedChallenge.typeId }}</Badge>
              </div>
              <p class="mt-1 text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.editChallengeDescription') }}
              </p>
            </div>
            <Button variant="outline" size="sm" @click="clearSelectedChallenge">
              <Plus class="mr-2 size-4" />
              {{ t('admin.competitionDetail.deployTitle') }}
            </Button>
          </div>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.markdownDescriptionShort') }}</Label>
              <Textarea v-model="selectedEdit.description" class="font-mono text-xs" rows="8" />
            </div>
          </div>

          <div class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-4">
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
              <Input
                v-model.number="selectedEdit.difficultyCoefficient"
                type="number"
                min="0.1"
                step="0.1"
              />
            </div>
            <div class="grid gap-2 lg:col-span-2">
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
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
              <Input v-model="selectedEdit.flagPrefix" placeholder="flag" />
            </div>
            <label
              v-if="competitionForm.gameModeType === 'Ctf'"
              class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm lg:col-span-4"
            >
              <input v-model="selectedEdit.enableBloodBonus" type="checkbox" class="size-4" />
              <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
            </label>
          </div>

          <DecayCurvePreview class="mt-4" :config="selectedEdit" />

          <div class="mt-4 grid gap-2 rounded-lg border bg-muted/30 p-3">
            <Label>{{ t('admin.challenges.orchestration') }}</Label>
            <Textarea v-model="selectedEdit.orchestrationJson" class="min-h-48 font-mono text-xs" />
            <p v-if="editOrchestrationError" class="text-xs text-destructive">
              {{ editOrchestrationError }}
            </p>
          </div>

          <div
            v-if="selectedIsPenetration"
            class="mt-4 grid gap-3 rounded-lg border bg-muted/30 p-3"
          >
            <div>
              <div class="text-sm font-semibold">{{ t('penetration.topology') }}</div>
              <p class="text-xs text-muted-foreground">{{ t('penetration.topologyHint') }}</p>
            </div>
            <div
              v-if="loadingSelectedPenetrationTopology"
              class="py-4 text-sm text-muted-foreground"
            >
              <Loader2 class="mr-2 inline size-4 animate-spin" />
              {{ t('common.loading') }}
            </div>
            <template v-else>
              <Textarea
                v-model="selectedPenetrationTopologyJson"
                class="min-h-80 font-mono text-xs"
              />
              <p v-if="selectedPenetrationTopologyError" class="text-xs text-destructive">
                {{ selectedPenetrationTopologyError }}
              </p>
              <Button
                variant="outline"
                :disabled="
                  updatePenetrationTopologyMutation.isPending.value ||
                  !selectedPenetrationTopologyJson.trim()
                "
                @click="updatePenetrationTopologyMutation.mutate()"
              >
                <Loader2
                  v-if="updatePenetrationTopologyMutation.isPending.value"
                  class="mr-2 size-4 animate-spin"
                />
                {{ t('common.save') }}
              </Button>
            </template>
          </div>

          <div
            v-if="competitionForm.gameModeType === 'Awdp'"
            class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-3"
          >
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label>
              <Input v-model.number="selectedEdit.awdpAttackScorePerRound" type="number" min="0" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label>
              <Input v-model.number="selectedEdit.awdpDefenseScorePerRound" type="number" min="0" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label>
              <Input v-model.number="selectedEdit.awdpFixTimeoutSeconds" type="number" min="1" />
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
          </div>

          <div class="mt-4 space-y-2">
            <div class="flex items-center justify-between">
              <Label>{{ t('admin.competitionDetail.hints') }}</Label>
              <Button variant="outline" size="sm" @click="addEditHint">
                <Plus class="mr-2 size-4" />
                {{ t('common.add') }}
              </Button>
            </div>
            <Input
              v-for="(_, index) in selectedEdit.hints"
              :key="index"
              v-model="selectedEdit.hints[index]"
              :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })"
            />
          </div>

          <div class="mt-5 flex justify-end">
            <Button
              :disabled="updateChallengeMutation.isPending.value"
              @click="updateChallengeMutation.mutate()"
            >
              <Loader2
                v-if="updateChallengeMutation.isPending.value"
                class="mr-2 size-4 animate-spin"
              />
              {{ t('admin.competitionDetail.saveDeployedChallenge') }}
            </Button>
          </div>
        </div>

        <div
          v-if="activeSection === 'challenges' && !selectedChallenge"
          class="noctf-workbench p-5"
        >
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
                <SelectTrigger
                  ><SelectValue :placeholder="t('admin.competitionDetail.selectTemplate')"
                /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="template in templates" :key="template.id" :value="template.id">
                    {{ template.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
              <Input
                v-model.number="bindForm.difficultyCoefficient"
                type="number"
                min="0.1"
                step="0.1"
              />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
              <Input v-model="bindForm.flagPrefix" placeholder="flag" />
            </div>
            <label
              v-if="competitionForm.gameModeType === 'Ctf'"
              class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm"
            >
              <input v-model="bindForm.enableBloodBonus" type="checkbox" class="size-4" />
              <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
            </label>
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.markdownDescription') }}</Label>
              <Textarea
                v-model="bindForm.description"
                class="font-mono text-xs"
                rows="5"
                :placeholder="t('admin.competitionDetail.descriptionPlaceholder')"
              />
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

          <div
            v-if="competitionForm.gameModeType === 'Awdp'"
            class="noctf-fieldset mt-4 grid gap-4 lg:grid-cols-3"
          >
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

          <div class="mt-4 grid gap-2 rounded-lg border bg-muted/30 p-3">
            <Label>{{ t('admin.challenges.orchestration') }}</Label>
            <Textarea v-model="bindForm.orchestrationJson" class="min-h-40 font-mono text-xs" />
            <p v-if="bindOrchestrationError" class="text-xs text-destructive">
              {{ bindOrchestrationError }}
            </p>
          </div>

          <div class="mt-4 space-y-2">
            <div class="flex items-center justify-between">
              <Label>{{ t('admin.competitionDetail.hints') }}</Label>
              <Button variant="outline" size="sm" @click="addBindHint">
                <Plus class="mr-2 size-4" />
                {{ t('admin.competitionDetail.addHint') }}
              </Button>
            </div>
            <Input
              v-for="(_, index) in bindForm.hints"
              :key="index"
              v-model="bindForm.hints[index]"
              :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })"
            />
          </div>

          <div class="mt-5 flex justify-end">
            <Button
              :disabled="bindMutation.isPending.value || !bindForm.templateId"
              @click="bindMutation.mutate()"
            >
              <Loader2 v-if="bindMutation.isPending.value" class="mr-2 size-4 animate-spin" />
              {{ t('admin.competitionDetail.deployChallenge') }}
            </Button>
          </div>
        </div>

        <div v-if="activeSection === 'teams'" class="noctf-workbench p-5">
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
                  <code class="mt-1 block text-[10px] text-muted-foreground">{{
                    team.inviteToken
                  }}</code>
                </TableCell>
                <TableCell>{{ team.memberCount }} / {{ competitionForm.maxTeamMembers }}</TableCell>
                <TableCell class="text-xs text-muted-foreground">
                  {{ team.trackName || '-' }}
                </TableCell>
                <TableCell>
                  <Badge
                    :variant="
                      team.registrationStatus === 'approved'
                        ? 'default'
                        : team.registrationStatus === 'rejected'
                          ? 'destructive'
                          : 'secondary'
                    "
                  >
                    {{ team.registrationStatus }}
                  </Badge>
                </TableCell>
                <TableCell>
                  <Badge :variant="team.isLocked ? 'outline' : 'secondary'">
                    {{
                      team.isLocked
                        ? t('admin.competitionDetail.locked')
                        : t('admin.competitionDetail.unlocked')
                    }}
                  </Badge>
                </TableCell>
                <TableCell>
                  <Badge :variant="team.isBanned ? 'destructive' : 'secondary'">
                    {{
                      team.isBanned
                        ? t('admin.competitionDetail.banned')
                        : t('admin.competitionDetail.normal')
                    }}
                  </Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-1">
                    <Button
                      v-if="team.registrationStatus !== 'approved'"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      @click="approveTeamMutation.mutate(team.id)"
                    >
                      <Check class="size-4" />
                    </Button>
                    <Button
                      v-if="team.registrationStatus !== 'rejected'"
                      variant="ghost"
                      size="icon"
                      class="size-8 text-destructive"
                      @click="rejectTeamMutation.mutate(team.id)"
                    >
                      <X class="size-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      @click="
                        lockTeamMutation.mutate({ teamId: team.id, isLocked: !team.isLocked })
                      "
                    >
                      <Unlock v-if="team.isLocked" class="size-4" />
                      <Lock v-else class="size-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :class="team.isBanned ? '' : 'text-destructive'"
                      @click="
                        team.isBanned
                          ? unbanTeamMutation.mutate(team.id)
                          : openBanTeam(team.id, team.name)
                      "
                    >
                      <ShieldAlert class="size-4" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div v-if="activeSection === 'instances'" class="noctf-workbench p-5">
          <div class="mb-5 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.penetrationInstancesTitle') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.penetrationInstancesDescription') }}
              </p>
            </div>
            <Button
              v-if="showInstanceOperations"
              variant="outline"
              size="sm"
              :disabled="loadingPenetrationInstances"
              @click="refetchPenetrationInstances()"
            >
              <RefreshCw
                class="mr-2 size-4"
                :class="loadingPenetrationInstances ? 'animate-spin' : ''"
              />
              {{ t('common.refresh') }}
            </Button>
          </div>

          <div
            v-if="!showInstanceOperations"
            class="noctf-state-box min-h-0 p-6 text-sm text-muted-foreground"
          >
            {{ t('admin.competitionDetail.noPenetrationInstances') }}
          </div>
          <template v-else>
            <div class="mb-4 grid gap-3 md:grid-cols-2">
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.challengeFilter') }}</Label>
                <Select v-model="penetrationInstanceFilters.challengeId">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">
                      {{ t('common.all') }}
                    </SelectItem>
                    <SelectItem
                      v-for="challenge in penetrationChallenges"
                      :key="challenge.id"
                      :value="challenge.id"
                    >
                      {{ challenge.title }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.teamFilter') }}</Label>
                <Select v-model="penetrationInstanceFilters.teamId">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">
                      {{ t('common.all') }}
                    </SelectItem>
                    <SelectItem
                      v-for="team in competitionTeams ?? []"
                      :key="team.id"
                      :value="team.id"
                    >
                      {{ team.name }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div class="w-full max-w-full overflow-x-auto">
              <Table class="min-w-[920px]">
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ t('admin.teams.name') }}</TableHead>
                    <TableHead>{{ t('admin.challenges.titleColumn') }}</TableHead>
                    <TableHead>{{ t('common.status') }}</TableHead>
                    <TableHead>{{ t('admin.competitionDetail.entry') }}</TableHead>
                    <TableHead>{{ t('admin.competitionDetail.resetCount') }}</TableHead>
                    <TableHead>{{ t('admin.competitionDetail.expiresAt') }}</TableHead>
                    <TableHead>{{ t('admin.competitionDetail.lastUpdated') }}</TableHead>
                    <TableHead class="text-right">
                      {{ t('common.actions') }}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-if="loadingPenetrationInstances">
                    <TableCell colspan="8" class="h-20 text-center text-muted-foreground">
                      <Loader2 class="mr-2 inline size-4 animate-spin" />
                      {{ t('common.loading') }}
                    </TableCell>
                  </TableRow>
                  <TableRow v-else-if="!penetrationInstances.length">
                    <TableCell colspan="8" class="h-20 text-center text-muted-foreground">
                      {{ t('admin.competitionDetail.noPenetrationInstances') }}
                    </TableCell>
                  </TableRow>
                  <TableRow v-for="instance in penetrationInstances" v-else :key="instance.id">
                    <TableCell>
                      <div class="font-medium">
                        {{ instance.teamName }}
                      </div>
                      <div class="text-xs text-muted-foreground">
                        {{ instance.teamId.slice(0, 8) }}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div class="font-medium">
                        {{ instance.challengeTitle }}
                      </div>
                      <div
                        v-if="instance.lastError"
                        class="mt-1 max-w-64 truncate text-xs text-destructive"
                        :title="instance.lastError"
                      >
                        {{ instance.lastError }}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Badge :variant="penetrationStatusVariant(instance.status)">
                        {{ instance.status }}
                      </Badge>
                    </TableCell>
                    <TableCell class="text-xs">
                      <a
                        v-if="isHttpUrl(instance.entryUrl)"
                        :href="instance.entryUrl || undefined"
                        target="_blank"
                        rel="noreferrer"
                        class="text-primary underline-offset-4 hover:underline"
                      >
                        {{ instance.entryUrl }}
                      </a>
                      <code
                        v-else-if="instance.entryUrl"
                        class="rounded bg-muted px-1.5 py-0.5 font-mono text-xs"
                        >{{ instance.entryUrl }}</code
                      >
                      <span v-else class="text-muted-foreground">-</span>
                    </TableCell>
                    <TableCell>{{ instance.resetCount }}</TableCell>
                    <TableCell class="text-xs text-muted-foreground">
                      {{ formatDate(instance.expiresAt) }}
                    </TableCell>
                    <TableCell class="text-xs text-muted-foreground">
                      {{ formatDate(instance.updatedAt) }}
                    </TableCell>
                    <TableCell class="text-right">
                      <div class="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8"
                          :disabled="resetPenetrationInstanceMutation.isPending.value"
                          @click="resetPenetrationInstanceMutation.mutate(instance.id)"
                        >
                          <Loader2
                            v-if="resetPenetrationInstanceMutation.isPending.value"
                            class="size-4 animate-spin"
                          />
                          <RotateCw v-else class="size-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8 text-destructive"
                          :disabled="destroyPenetrationInstanceMutation.isPending.value"
                          @click="openDestroyInstance(instance)"
                        >
                          <Loader2
                            v-if="destroyPenetrationInstanceMutation.isPending.value"
                            class="size-4 animate-spin"
                          />
                          <Trash2 v-else class="size-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </template>
        </div>

        <div v-if="activeSection === 'logs'" class="space-y-6">
          <div class="noctf-workbench">
            <div class="border-b border-border/80 p-4">
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.cheatTitle') }}
              </h3>
            </div>
            <div>
              <div
                v-if="loadingCheatIncidents"
                class="py-8 text-center text-sm text-muted-foreground"
              >
                <Loader2 class="mr-2 inline size-4 animate-spin" />
                {{ t('common.loading') }}
              </div>
              <div
                v-else-if="!cheatIncidents?.length"
                class="noctf-state-box min-h-0 p-6 text-sm text-muted-foreground"
              >
                {{ t('admin.competitionDetail.noCheatIncidents') }}
              </div>
              <div v-else>
                <div
                  v-for="incident in cheatIncidents"
                  :key="incident.id"
                  class="border-b border-border/70 p-4 last:border-b-0"
                >
                  <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div class="min-w-0 space-y-3">
                      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                        <p class="font-semibold leading-6">
                          {{
                            t('admin.competitionDetail.cheatSubmitSentence', {
                              suspect: incident.suspectTeamName,
                              victim: incidentVictimTeamName(incident),
                            })
                          }}
                        </p>
                        <span
                          v-if="incident.resolved"
                          class="text-xs font-medium text-muted-foreground"
                        >
                          {{ t('admin.competitionDetail.cheatResolved') }}
                        </span>
                      </div>
                      <dl
                        class="grid gap-x-6 gap-y-2 text-sm text-muted-foreground md:grid-cols-2 xl:grid-cols-4"
                      >
                        <div>
                          <dt class="text-xs font-medium text-foreground">
                            {{ t('admin.competitionDetail.cheatChallenge') }}
                          </dt>
                          <dd class="truncate">
                            {{ incident.challengeTitle }}
                          </dd>
                        </div>
                        <div>
                          <dt class="text-xs font-medium text-foreground">
                            {{ t('admin.competitionDetail.cheatUser') }}
                          </dt>
                          <dd class="truncate">
                            {{ incident.userName }}
                          </dd>
                        </div>
                        <div>
                          <dt class="text-xs font-medium text-foreground">
                            {{ t('admin.competitionDetail.cheatReason') }}
                          </dt>
                          <dd class="truncate">
                            {{ incident.reason }}
                          </dd>
                        </div>
                        <div>
                          <dt class="text-xs font-medium text-foreground">
                            {{ t('admin.competitionDetail.cheatTime') }}
                          </dt>
                          <dd>
                            {{ new Date(incident.createdAt).toLocaleString() }}
                          </dd>
                        </div>
                      </dl>
                      <div v-if="incident.submittedFlag" class="text-xs text-muted-foreground">
                        <span class="font-medium text-foreground">
                          {{ t('admin.competitionDetail.cheatSubmittedFlag') }}
                        </span>
                        <span class="ml-2 break-all font-mono">
                          {{ incident.submittedFlag }}
                        </span>
                      </div>
                    </div>
                    <div class="flex shrink-0 flex-wrap gap-2">
                      <Button
                        size="sm"
                        variant="destructive"
                        :disabled="
                          banTeamMutation.isPending.value || banTeamsMutation.isPending.value
                        "
                        @click="openBanTeam(incident.suspectTeamId, incident.suspectTeamName)"
                      >
                        <ShieldAlert class="mr-2 size-4" />
                        {{ t('admin.competitionDetail.banSuspectTeam') }}
                      </Button>
                      <Button
                        v-if="canBanVictimTeam(incident)"
                        size="sm"
                        variant="outline"
                        :disabled="
                          banTeamMutation.isPending.value || banTeamsMutation.isPending.value
                        "
                        @click="openBanVictimTeam(incident)"
                      >
                        <ShieldAlert class="mr-2 size-4" />
                        {{ t('admin.competitionDetail.banVictimTeam') }}
                      </Button>
                      <Button
                        v-if="canBanVictimTeam(incident)"
                        size="sm"
                        variant="destructive"
                        :disabled="
                          banTeamMutation.isPending.value || banTeamsMutation.isPending.value
                        "
                        @click="openBanBothTeams(incident)"
                      >
                        <ShieldAlert class="mr-2 size-4" />
                        {{ t('admin.competitionDetail.banBothTeams') }}
                      </Button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div class="noctf-workbench">
            <div class="border-b border-border/80 p-4">
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.logsTitle') }}
              </h3>
            </div>
            <div>
              <div v-if="loadingLogs" class="py-8 text-center text-sm text-muted-foreground">
                <Loader2 class="mr-2 inline size-4 animate-spin" />
                {{ t('common.loading') }}
              </div>
              <div v-else class="noctf-scrollbar max-h-[560px] overflow-y-auto">
                <div
                  v-if="!competitionLogs?.length"
                  class="noctf-state-box min-h-0 p-6 text-sm text-muted-foreground"
                >
                  {{ t('admin.competitionDetail.noLogs') }}
                </div>
                <div
                  v-for="log in competitionLogs ?? []"
                  :key="log.id"
                  class="border-b border-border/70 p-4 text-sm last:border-b-0"
                >
                  <p class="font-medium leading-6">
                    {{ log.message }}
                  </p>
                  <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                    <span>{{ new Date(log.createdAt).toLocaleString() }}</span>
                    <span v-if="log.teamName">
                      {{ t('admin.competitionDetail.logTeam', { team: log.teamName }) }}
                    </span>
                    <span v-if="log.challengeTitle">
                      {{
                        t('admin.competitionDetail.logChallenge', {
                          challenge: log.challengeTitle,
                        })
                      }}
                    </span>
                    <span v-if="log.level && log.level !== 'info'">
                      {{ t('admin.competitionDetail.logLevel', { level: log.level }) }}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <aside v-if="activeSection === 'challenges'" class="space-y-4">
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
                  {{ challenge.pointsConfig.minimumPoints }} →
                  {{ challenge.pointsConfig.initialPoints }}
                </TableCell>
                <TableCell>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8 text-destructive"
                    @click.stop="openDeleteChallenge(challenge)"
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
                    <Loader2
                      v-if="restartContainerMutation.isPending.value"
                      class="size-4 animate-spin"
                    />
                    <Save v-else class="size-4" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </aside>
    </div>

    <Dialog v-model:open="dangerDialogOpen">
      <DialogContent class="sm:max-w-[520px]">
        <DialogHeader>
          <DialogTitle class="flex items-center gap-2 text-destructive">
            <ShieldAlert class="size-5" />
            {{ pendingDangerAction?.title }}
          </DialogTitle>
          <DialogDescription>
            {{ pendingDangerAction?.description }}
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-3">
          <div class="noctf-danger-panel text-sm">
            {{ t('admin.competitionDetail.confirmDangerHint') }}
          </div>
          <label class="grid gap-2 text-sm">
            <span class="font-medium">{{ t('admin.competitionDetail.confirmDangerLabel') }}</span>
            <code class="rounded-md bg-muted px-2 py-1 font-mono text-xs text-muted-foreground">
              {{ pendingDangerAction?.confirmLabel }}
            </code>
            <Input v-model="dangerConfirmText" class="font-mono text-xs" autocomplete="off" />
          </label>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="dangerDialogOpen = false">
            {{ t('common.cancel') }}
          </Button>
          <Button variant="destructive" :disabled="!dangerConfirmed" @click="confirmDangerAction">
            {{ t('common.confirm') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
