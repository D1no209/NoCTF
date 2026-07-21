import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { numberOrDefault, optionalNumber } from '../shared/number'

export interface PointsConfigDto {
  initialPoints: number
  minimumPoints: number
  decayFactor: number
  decayFunction: string
}

export interface AdminCompetitionDto {
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

export interface AdminCompetitionTemplateDto {
  id: string
  title: string
  description?: string
  typeId: string
}

export interface ChallengeHintDto {
  id?: string
  content: string
  displayOrder?: number
}

export interface CompetitionChallengeDto {
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

export interface CompetitionTeamDto {
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

export interface CompetitionLogDto {
  id: string
  level: string
  eventType: string
  message: string
  teamName?: string | null
  challengeTitle?: string | null
  createdAt: string
}

export interface CheatIncidentDto {
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

// Numeric fields are canonical strings in feature form state (see contract).
export interface AdminCompetitionDetailForm {
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

export interface AdminCompetitionChallengeBindForm {
  templateId: string
  description: string
  initialPoints: string
  minimumPoints: string
  decayFactor: string
  decayFunction: string
  difficultyCoefficient: string
  enableBloodBonus: boolean
  flagPrefix: string
  awdpAttackScorePerRound: string
  awdpDefenseScorePerRound: string
  awdpMaxAttackAttempts: string
  awdpMaxDefenseAttempts: string
  awdpFixEntry: string
  awdpFixTimeoutSeconds: string
  hints: string[]
}

export interface AdminCompetitionChallengeEdit {
  description: string
  initialPoints: string
  minimumPoints: string
  decayFactor: string
  decayFunction: string
  difficultyCoefficient: string
  enableBloodBonus: boolean
  flagPrefix: string
  awdpAttackScorePerRound: string
  awdpDefenseScorePerRound: string
  awdpMaxAttackAttempts: string
  awdpMaxDefenseAttempts: string
  awdpFixEntry: string
  awdpFixTimeoutSeconds: string
  hints: string[]
}

export const adminCompetitionDetailSections = ['settings', 'challenges', 'teams', 'cheats', 'logs'] as const
export type AdminCompetitionDetailSection = typeof adminCompetitionDetailSections[number]

const trackNamesSeparatorPattern = /\r?\n|,/

function toDateTimeLocal(value: string) {
  if (!value)
    return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function optionalString(value: number | string | undefined | null) {
  return value === undefined || value === null ? '' : String(value)
}

// V1's `??` inheritance chain: only null/undefined fall through, '' is kept.
function inheritNumberString(...candidates: (number | string | undefined | null)[]) {
  for (const candidate of candidates) {
    if (candidate !== undefined && candidate !== null)
      return String(candidate)
  }
  return ''
}

function cleanHints(hints: string[]) {
  return hints.map(hint => hint.trim()).filter(Boolean)
}

export function useAdminCompetitionDetailPage() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()
  const competitionId = computed(() => String(route.params.id))
  const selectedChallengeId = ref<string | null>(null)

  const competitionForm = ref<AdminCompetitionDetailForm>({
    title: '',
    description: '',
    gameModeType: 'Ctf',
    status: 'Draft',
    startTime: '',
    endTime: '',
    initialPoints: '500',
    minimumPoints: '100',
    decayFactor: '450',
    decayFunction: 'sigmoid',
    difficultyCoefficient: '1',
    firstBloodBonusPercent: '0',
    secondBloodBonusPercent: '0',
    thirdBloodBonusPercent: '0',
    teamRegistrationAutoApprove: true,
    maxTeamMembers: '5',
    tracksEnabled: false,
    trackNamesText: '',
    roundDurationSeconds: '',
    totalRounds: '',
    flagFormat: '',
    flagPath: '',
    attackPoints: '',
    serviceOnlinePoints: '',
    serviceDownPenalty: '',
    beenAttackedPenalty: '',
    flagValidityRounds: '',
    awdpAttackScorePerRound: '',
    awdpDefenseScorePerRound: '',
    awdpMaxAttackAttempts: '',
    awdpMaxDefenseAttempts: '',
    awdpAllowAttackAfterBreakSuccess: false,
    awdpAllowDefenseAfterFixSuccess: false,
    awdpServicePenaltyEnabled: false,
    awdpServicePenaltyPerRound: '',
    awdpViolationPenaltyEnabled: false,
    awdpViolationPenalty: '',
    awdpFixEntry: 'fix.sh',
    awdpFixTimeoutSeconds: '',
  })

  const bindForm = ref<AdminCompetitionChallengeBindForm>({
    templateId: '',
    description: '',
    initialPoints: '500',
    minimumPoints: '100',
    decayFactor: '450',
    decayFunction: 'sigmoid',
    difficultyCoefficient: '1',
    enableBloodBonus: false,
    flagPrefix: 'flag',
    awdpAttackScorePerRound: '50',
    awdpDefenseScorePerRound: '100',
    awdpMaxAttackAttempts: '5',
    awdpMaxDefenseAttempts: '3',
    awdpFixEntry: 'fix.sh',
    awdpFixTimeoutSeconds: '60',
    hints: [''],
  })

  const selectedEdit = ref<AdminCompetitionChallengeEdit>({
    description: '',
    initialPoints: '500',
    minimumPoints: '100',
    decayFactor: '450',
    decayFunction: 'sigmoid',
    difficultyCoefficient: '1',
    enableBloodBonus: false,
    flagPrefix: 'flag',
    awdpAttackScorePerRound: '50',
    awdpDefenseScorePerRound: '100',
    awdpMaxAttackAttempts: '5',
    awdpMaxDefenseAttempts: '3',
    awdpFixEntry: 'fix.sh',
    awdpFixTimeoutSeconds: '60',
    hints: [''],
  })

  const competitionQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetition(competitionId.value)),
    queryFn: () => adminApi.competition<AdminCompetitionDto>(competitionId.value),
  })

  const templatesQuery = useQuery({
    queryKey: queryKeys.adminChallenges,
    queryFn: () => adminApi.challenges<AdminCompetitionTemplateDto[]>(),
  })

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionChallenges(competitionId.value)),
    queryFn: () => adminApi.competitionChallenges<CompetitionChallengeDto[]>(competitionId.value),
  })

  const teamsQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionTeams(competitionId.value)),
    queryFn: () => adminApi.competitionTeams<CompetitionTeamDto[]>(competitionId.value),
  })

  const logsQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionLogs(competitionId.value)),
    queryFn: () => adminApi.competitionLogs<CompetitionLogDto[]>(competitionId.value),
  })

  const cheatsQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionCheatIncidents(competitionId.value)),
    queryFn: () => adminApi.competitionCheatIncidents<CheatIncidentDto[]>(competitionId.value),
  })

  watch(competitionQuery.data, (value) => {
    if (!value)
      return
    competitionForm.value = {
      title: value.title,
      description: value.description ?? '',
      gameModeType: value.gameModeType,
      status: value.status,
      startTime: toDateTimeLocal(value.startTime),
      endTime: toDateTimeLocal(value.endTime),
      initialPoints: String(value.defaultPointsConfig?.initialPoints ?? 500),
      minimumPoints: String(value.defaultPointsConfig?.minimumPoints ?? 100),
      decayFactor: String(value.defaultPointsConfig?.decayFactor ?? 450),
      decayFunction: value.defaultPointsConfig?.decayFunction ?? 'sigmoid',
      difficultyCoefficient: String(value.difficultyCoefficient ?? 1),
      firstBloodBonusPercent: String(value.firstBloodBonusPercent ?? 0),
      secondBloodBonusPercent: String(value.secondBloodBonusPercent ?? 0),
      thirdBloodBonusPercent: String(value.thirdBloodBonusPercent ?? 0),
      teamRegistrationAutoApprove: value.teamRegistrationAutoApprove ?? true,
      maxTeamMembers: String(value.maxTeamMembers ?? 5),
      tracksEnabled: value.tracksEnabled ?? false,
      trackNamesText: (value.trackNames ?? []).join('\n'),
      roundDurationSeconds: optionalString(value.roundDurationSeconds),
      totalRounds: optionalString(value.totalRounds),
      flagFormat: value.flagFormat ?? '',
      flagPath: value.flagPath ?? '',
      attackPoints: optionalString(value.attackPoints),
      serviceOnlinePoints: optionalString(value.serviceOnlinePoints),
      serviceDownPenalty: optionalString(value.serviceDownPenalty),
      beenAttackedPenalty: optionalString(value.beenAttackedPenalty),
      flagValidityRounds: optionalString(value.flagValidityRounds),
      awdpAttackScorePerRound: optionalString(value.awdpAttackScorePerRound),
      awdpDefenseScorePerRound: optionalString(value.awdpDefenseScorePerRound),
      awdpMaxAttackAttempts: optionalString(value.awdpMaxAttackAttempts),
      awdpMaxDefenseAttempts: optionalString(value.awdpMaxDefenseAttempts),
      awdpAllowAttackAfterBreakSuccess: value.awdpAllowAttackAfterBreakSuccess ?? false,
      awdpAllowDefenseAfterFixSuccess: value.awdpAllowDefenseAfterFixSuccess ?? false,
      awdpServicePenaltyEnabled: value.awdpServicePenaltyEnabled ?? false,
      awdpServicePenaltyPerRound: optionalString(value.awdpServicePenaltyPerRound),
      awdpViolationPenaltyEnabled: value.awdpViolationPenaltyEnabled ?? false,
      awdpViolationPenalty: optionalString(value.awdpViolationPenalty),
      awdpFixEntry: value.awdpFixEntry ?? 'fix.sh',
      awdpFixTimeoutSeconds: optionalString(value.awdpFixTimeoutSeconds),
    }
    bindForm.value = {
      ...bindForm.value,
      initialPoints: competitionForm.value.initialPoints,
      minimumPoints: competitionForm.value.minimumPoints,
      decayFactor: competitionForm.value.decayFactor,
      decayFunction: competitionForm.value.decayFunction,
      difficultyCoefficient: competitionForm.value.difficultyCoefficient,
      flagPrefix: 'flag',
      awdpAttackScorePerRound: String(value.awdpAttackScorePerRound ?? 50),
      awdpDefenseScorePerRound: String(value.awdpDefenseScorePerRound ?? 100),
      awdpMaxAttackAttempts: String(value.awdpMaxAttackAttempts ?? 5),
      awdpMaxDefenseAttempts: String(value.awdpMaxDefenseAttempts ?? 3),
      awdpFixEntry: value.awdpFixEntry ?? 'fix.sh',
      awdpFixTimeoutSeconds: String(value.awdpFixTimeoutSeconds ?? 60),
    }
  }, { immediate: true })

  watch(templatesQuery.data, (items) => {
    if (!bindForm.value.templateId && items?.length)
      bindForm.value = { ...bindForm.value, templateId: items[0].id }
  }, { immediate: true })

  const selectedChallenge = computed(() =>
    challengesQuery.data.value?.find(challenge => challenge.id === selectedChallengeId.value) ?? null)

  watch(selectedChallenge, (challenge) => {
    if (!challenge)
      return
    selectedEdit.value = {
      description: challenge.description ?? '',
      initialPoints: String(challenge.pointsConfig?.initialPoints ?? 500),
      minimumPoints: String(challenge.pointsConfig?.minimumPoints ?? 100),
      decayFactor: String(challenge.pointsConfig?.decayFactor ?? 450),
      decayFunction: challenge.pointsConfig?.decayFunction ?? 'sigmoid',
      difficultyCoefficient: String(challenge.difficultyCoefficient ?? 1),
      enableBloodBonus: challenge.enableBloodBonus ?? false,
      flagPrefix: challenge.flagPrefix ?? 'flag',
      awdpAttackScorePerRound: inheritNumberString(challenge.awdpAttackScorePerRound, competitionForm.value.awdpAttackScorePerRound, 50),
      awdpDefenseScorePerRound: inheritNumberString(challenge.awdpDefenseScorePerRound, competitionForm.value.awdpDefenseScorePerRound, 100),
      awdpMaxAttackAttempts: inheritNumberString(challenge.awdpMaxAttackAttempts, competitionForm.value.awdpMaxAttackAttempts, 5),
      awdpMaxDefenseAttempts: inheritNumberString(challenge.awdpMaxDefenseAttempts, competitionForm.value.awdpMaxDefenseAttempts, 3),
      awdpFixEntry: challenge.awdpFixEntry ?? competitionForm.value.awdpFixEntry ?? 'fix.sh',
      awdpFixTimeoutSeconds: inheritNumberString(challenge.awdpFixTimeoutSeconds, competitionForm.value.awdpFixTimeoutSeconds, 60),
      hints: challenge.hints?.length ? challenge.hints.map(hint => hint.content) : [''],
    }
  }, { immediate: true })

  function competitionPayload() {
    const form = competitionForm.value
    return {
      title: form.title.trim(),
      description: form.description.trim() || undefined,
      gameModeType: form.gameModeType,
      status: form.status,
      startTime: new Date(form.startTime).toISOString(),
      endTime: new Date(form.endTime).toISOString(),
      defaultPointsConfig: {
        initialPoints: numberOrDefault(form.initialPoints, 500),
        minimumPoints: numberOrDefault(form.minimumPoints, 100),
        decayFactor: numberOrDefault(form.decayFactor, 450),
        decayFunction: form.decayFunction || 'sigmoid',
      },
      difficultyCoefficient: numberOrDefault(form.difficultyCoefficient, 1),
      firstBloodBonusPercent: numberOrDefault(form.firstBloodBonusPercent, 0),
      secondBloodBonusPercent: numberOrDefault(form.secondBloodBonusPercent, 0),
      thirdBloodBonusPercent: numberOrDefault(form.thirdBloodBonusPercent, 0),
      teamRegistrationAutoApprove: form.teamRegistrationAutoApprove,
      maxTeamMembers: numberOrDefault(form.maxTeamMembers, 5),
      tracksEnabled: form.tracksEnabled,
      trackNames: form.trackNamesText.split(trackNamesSeparatorPattern).map(item => item.trim()).filter(Boolean),
      roundDurationSeconds: optionalNumber(form.roundDurationSeconds),
      totalRounds: optionalNumber(form.totalRounds),
      flagFormat: form.flagFormat.trim() || undefined,
      flagPath: form.flagPath.trim() || undefined,
      attackPoints: optionalNumber(form.attackPoints),
      serviceOnlinePoints: optionalNumber(form.serviceOnlinePoints),
      serviceDownPenalty: optionalNumber(form.serviceDownPenalty),
      beenAttackedPenalty: optionalNumber(form.beenAttackedPenalty),
      flagValidityRounds: optionalNumber(form.flagValidityRounds),
      awdpAttackScorePerRound: optionalNumber(form.awdpAttackScorePerRound),
      awdpDefenseScorePerRound: optionalNumber(form.awdpDefenseScorePerRound),
      awdpMaxAttackAttempts: optionalNumber(form.awdpMaxAttackAttempts),
      awdpMaxDefenseAttempts: optionalNumber(form.awdpMaxDefenseAttempts),
      awdpAllowAttackAfterBreakSuccess: form.awdpAllowAttackAfterBreakSuccess,
      awdpAllowDefenseAfterFixSuccess: form.awdpAllowDefenseAfterFixSuccess,
      awdpServicePenaltyEnabled: form.awdpServicePenaltyEnabled,
      awdpServicePenaltyPerRound: optionalNumber(form.awdpServicePenaltyPerRound),
      awdpViolationPenaltyEnabled: form.awdpViolationPenaltyEnabled,
      awdpViolationPenalty: optionalNumber(form.awdpViolationPenalty),
      awdpFixEntry: form.awdpFixEntry.trim() || undefined,
      awdpFixTimeoutSeconds: optionalNumber(form.awdpFixTimeoutSeconds),
    }
  }

  function challengeEditorPayload(editor: AdminCompetitionChallengeEdit) {
    return {
      description: editor.description.trim() || undefined,
      descriptionFormat: 'markdown',
      pointsConfig: {
        initialPoints: numberOrDefault(editor.initialPoints, 500),
        minimumPoints: numberOrDefault(editor.minimumPoints, 100),
        decayFactor: numberOrDefault(editor.decayFactor, 450),
        decayFunction: editor.decayFunction || 'sigmoid',
      },
      difficultyCoefficient: numberOrDefault(editor.difficultyCoefficient, 1),
      enableBloodBonus: editor.enableBloodBonus,
      flagPrefix: editor.flagPrefix.trim() || 'flag',
      awdpAttackScorePerRound: optionalNumber(editor.awdpAttackScorePerRound),
      awdpDefenseScorePerRound: optionalNumber(editor.awdpDefenseScorePerRound),
      awdpMaxAttackAttempts: optionalNumber(editor.awdpMaxAttackAttempts),
      awdpMaxDefenseAttempts: optionalNumber(editor.awdpMaxDefenseAttempts),
      awdpFixEntry: editor.awdpFixEntry.trim() || undefined,
      awdpFixTimeoutSeconds: optionalNumber(editor.awdpFixTimeoutSeconds),
      hints: cleanHints(editor.hints),
    }
  }

  const saveCompetitionMutation = useMutation({
    mutationFn: () => adminApi.updateCompetition(competitionId.value, competitionPayload()),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetition(competitionId.value) })
    },
  })

  const bindMutation = useMutation({
    mutationFn: () => adminApi.bindCompetitionChallenge(competitionId.value, {
      templateId: bindForm.value.templateId,
      ...challengeEditorPayload(bindForm.value),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
      bindForm.value = { ...bindForm.value, description: '', hints: [''] }
    },
  })

  const updateChallengeMutation = useMutation({
    mutationFn: () => adminApi.updateCompetitionChallenge(
      competitionId.value,
      selectedChallenge.value!.id,
      challengeEditorPayload(selectedEdit.value),
    ),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    },
  })

  const deleteChallengeMutation = useMutation({
    mutationFn: (challengeId: string) => adminApi.deleteCompetitionChallenge(competitionId.value, challengeId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
      selectedChallengeId.value = null
    },
  })

  const approveTeamMutation = useMutation({
    mutationFn: (teamId: string) => adminApi.approveCompetitionTeam(competitionId.value, teamId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    },
  })

  const rejectTeamMutation = useMutation({
    mutationFn: (teamId: string) => adminApi.rejectCompetitionTeam(competitionId.value, teamId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    },
  })

  const lockTeamMutation = useMutation({
    mutationFn: ({ teamId, isLocked }: { teamId: string, isLocked: boolean }) =>
      adminApi.setCompetitionTeamLock(competitionId.value, teamId, isLocked),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
    },
  })

  const banTeamMutation = useMutation({
    mutationFn: (teamId: string) => adminApi.banCompetitionTeam(competitionId.value, teamId, 'suspected_cheat'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    },
  })

  const unbanTeamMutation = useMutation({
    mutationFn: (teamId: string) => adminApi.unbanCompetitionTeam(competitionId.value, teamId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionLogs(competitionId.value) })
    },
  })

  const restartContainerMutation = useMutation({
    mutationFn: (challengeId: string) => adminApi.restartCompetitionChallengeContainer(competitionId.value, challengeId),
  })

  const rebuildScoreboardMutation = useMutation({
    mutationFn: () => adminApi.rebuildScoreboard(competitionId.value) as Promise<{ rows?: number }>,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) })
    },
  })

  const activeSection = computed<AdminCompetitionDetailSection>(() => {
    const section = route.query.section
    if (typeof section === 'string' && (adminCompetitionDetailSections as readonly string[]).includes(section))
      return section as AdminCompetitionDetailSection
    return 'settings'
  })

  const canOpenAwdpScreen = computed(() =>
    (competitionQuery.data.value?.gameModeType ?? competitionForm.value.gameModeType).toLowerCase() === 'awdp')

  function switchSection(section: string) {
    void router.replace({
      name: 'admin-competition-detail',
      params: { id: competitionId.value },
      query: { ...route.query, section },
    })
  }

  function isStaticContainer(challenge: CompetitionChallengeDto) {
    return challenge.deploymentType === 'StaticContainer' || challenge.deploymentType === 3
  }

  function selectChallenge(challenge: CompetitionChallengeDto) {
    selectedChallengeId.value = challenge.id
  }

  function addBindHint() {
    bindForm.value = { ...bindForm.value, hints: [...bindForm.value.hints, ''] }
  }

  function addEditHint() {
    selectedEdit.value = { ...selectedEdit.value, hints: [...selectedEdit.value.hints, ''] }
  }

  function goAdminCompetitions() {
    void router.push({ name: 'admin-competitions' })
  }

  function goOperations() {
    void router.push({ name: 'admin-competition-operations', params: { id: competitionId.value } })
  }

  function goAwdpScreen() {
    void router.push({ name: 'awdp-screen', params: { gameId: competitionId.value } })
  }

  return {
    competitionId,
    selectedChallengeId,
    competitionForm,
    bindForm,
    selectedEdit,
    competition: competitionQuery.data,
    loadingCompetition: competitionQuery.isLoading,
    templates: templatesQuery.data,
    competitionChallenges: challengesQuery.data,
    loadingChallenges: challengesQuery.isLoading,
    competitionTeams: teamsQuery.data,
    loadingTeams: teamsQuery.isLoading,
    competitionLogs: logsQuery.data,
    loadingLogs: logsQuery.isLoading,
    cheatIncidents: cheatsQuery.data,
    loadingCheatIncidents: cheatsQuery.isLoading,
    selectedChallenge,
    saveCompetitionMutation,
    bindMutation,
    updateChallengeMutation,
    deleteChallengeMutation,
    approveTeamMutation,
    rejectTeamMutation,
    lockTeamMutation,
    banTeamMutation,
    unbanTeamMutation,
    restartContainerMutation,
    rebuildScoreboardMutation,
    activeSection,
    switchSection,
    canOpenAwdpScreen,
    isStaticContainer,
    selectChallenge,
    addBindHint,
    addEditHint,
    goAdminCompetitions,
    goOperations,
    goAwdpScreen,
  }
}
