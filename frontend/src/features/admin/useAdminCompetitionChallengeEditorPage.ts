import { computed, reactive, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { challengeDirectionsForType, normalizeDirection } from '@/lib/challengeDirections'
import type { AdminCompetitionDto, CompetitionChallengeDto } from './useAdminCompetitionDetailPage'

export interface ChallengeTemplateDto {
  id: string
  title: string
  description?: string
  typeId: string
  direction: string
}

export interface AdminCompetitionChallengeEditorForm {
  templateId: string
  direction: string
  description: string
  initialPoints: number
  minimumPoints: number
  decayFactor: number
  decayFunction: string
  difficultyCoefficient: number
  enableBloodBonus: boolean
  flagPrefix: string
  awdpAttackScorePerRound: number | undefined
  awdpDefenseScorePerRound: number | undefined
  awdpMaxAttackAttempts: number | undefined
  awdpMaxDefenseAttempts: number | undefined
  awdpFixEntry: string
  awdpFixTimeoutSeconds: number | undefined
  hints: string[]
}

function optionalNumber(value: number | undefined) {
  return value === undefined ? undefined : Number(value)
}

// Canonical behavior follows V1's AdminCompetitionChallengeEditorWorkspace:
// template-driven creation or in-place editing of a competition challenge,
// with awdp defaults hydrated from the parent competition.
export function useAdminCompetitionChallengeEditorPage() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()
  const competitionId = computed(() => String(route.params.id))
  const challengeId = computed(() => typeof route.params.challengeId === 'string' ? route.params.challengeId : '')
  const editing = computed(() => Boolean(challengeId.value))

  const form = reactive<AdminCompetitionChallengeEditorForm>({
    templateId: '',
    direction: 'WEB',
    description: '',
    initialPoints: 500,
    minimumPoints: 100,
    decayFactor: 450,
    decayFunction: 'sigmoid',
    difficultyCoefficient: 1,
    enableBloodBonus: false,
    flagPrefix: 'flag',
    awdpAttackScorePerRound: 50,
    awdpDefenseScorePerRound: 100,
    awdpMaxAttackAttempts: 5,
    awdpMaxDefenseAttempts: 3,
    awdpFixEntry: 'fix.sh',
    awdpFixTimeoutSeconds: 60,
    hints: [''],
  })

  const competitionQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetition(competitionId.value)),
    queryFn: () => adminApi.competition<AdminCompetitionDto>(competitionId.value),
  })

  const templatesQuery = useQuery({
    queryKey: queryKeys.adminChallenges,
    queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
    enabled: computed(() => !editing.value),
  })

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionChallenges(competitionId.value)),
    queryFn: () => adminApi.competitionChallenges<CompetitionChallengeDto[]>(competitionId.value),
    enabled: computed(() => editing.value),
  })

  const selectedTemplate = computed(() => templatesQuery.data.value?.find(template => template.id === form.templateId))
  const selectedChallenge = computed(() => challengesQuery.data.value?.find(challenge => challenge.id === challengeId.value))
  const activeTypeId = computed(() => editing.value ? selectedChallenge.value?.typeId : selectedTemplate.value?.typeId)
  const directionOptions = computed(() => challengeDirectionsForType(activeTypeId.value))
  const loading = computed(() =>
    competitionQuery.isLoading.value || (editing.value ? challengesQuery.isLoading.value : templatesQuery.isLoading.value))

  watch(templatesQuery.data, (items) => {
    if (!editing.value && !form.templateId && items?.length)
      form.templateId = items[0].id
  }, { immediate: true })

  watch(selectedTemplate, (template) => {
    if (!template || editing.value)
      return
    form.direction = normalizeDirection(template.direction)
  }, { immediate: true })

  watch(competitionQuery.data, (value) => {
    if (!value)
      return
    form.awdpAttackScorePerRound = value.awdpAttackScorePerRound ?? 50
    form.awdpDefenseScorePerRound = value.awdpDefenseScorePerRound ?? 100
    form.awdpMaxAttackAttempts = value.awdpMaxAttackAttempts ?? 5
    form.awdpMaxDefenseAttempts = value.awdpMaxDefenseAttempts ?? 3
    form.awdpFixEntry = value.awdpFixEntry ?? 'fix.sh'
    form.awdpFixTimeoutSeconds = value.awdpFixTimeoutSeconds ?? 60
  }, { immediate: true })

  watch(selectedChallenge, (challenge) => {
    if (!challenge)
      return
    form.templateId = challenge.templateId ?? ''
    form.direction = normalizeDirection(challenge.direction)
    form.description = challenge.description ?? ''
    form.initialPoints = challenge.pointsConfig.initialPoints
    form.minimumPoints = challenge.pointsConfig.minimumPoints
    form.decayFactor = challenge.pointsConfig.decayFactor
    form.decayFunction = challenge.pointsConfig.decayFunction
    form.difficultyCoefficient = challenge.difficultyCoefficient
    form.enableBloodBonus = challenge.enableBloodBonus
    form.flagPrefix = challenge.flagPrefix ?? 'flag'
    form.awdpAttackScorePerRound = challenge.awdpAttackScorePerRound ?? form.awdpAttackScorePerRound
    form.awdpDefenseScorePerRound = challenge.awdpDefenseScorePerRound ?? form.awdpDefenseScorePerRound
    form.awdpMaxAttackAttempts = challenge.awdpMaxAttackAttempts ?? form.awdpMaxAttackAttempts
    form.awdpMaxDefenseAttempts = challenge.awdpMaxDefenseAttempts ?? form.awdpMaxDefenseAttempts
    form.awdpFixEntry = challenge.awdpFixEntry ?? form.awdpFixEntry
    form.awdpFixTimeoutSeconds = challenge.awdpFixTimeoutSeconds ?? form.awdpFixTimeoutSeconds
    form.hints = challenge.hints.length ? challenge.hints.map(hint => hint.content) : ['']
  }, { immediate: true })

  function payload() {
    return {
      direction: normalizeDirection(form.direction),
      description: form.description.trim() || undefined,
      descriptionFormat: 'markdown',
      flagPrefix: form.flagPrefix.trim() || 'flag',
      pointsConfig: {
        initialPoints: Number(form.initialPoints),
        minimumPoints: Number(form.minimumPoints),
        decayFactor: Number(form.decayFactor),
        decayFunction: form.decayFunction,
      },
      difficultyCoefficient: Number(form.difficultyCoefficient),
      enableBloodBonus: form.enableBloodBonus,
      awdpAttackScorePerRound: optionalNumber(form.awdpAttackScorePerRound),
      awdpDefenseScorePerRound: optionalNumber(form.awdpDefenseScorePerRound),
      awdpMaxAttackAttempts: optionalNumber(form.awdpMaxAttackAttempts),
      awdpMaxDefenseAttempts: optionalNumber(form.awdpMaxDefenseAttempts),
      awdpFixEntry: form.awdpFixEntry.trim() || undefined,
      awdpFixTimeoutSeconds: optionalNumber(form.awdpFixTimeoutSeconds),
      hints: form.hints.map(hint => hint.trim()).filter(Boolean),
    }
  }

  function returnToChallenges() {
    return router.push({ name: 'admin-competition-detail', params: { id: competitionId.value }, query: { section: 'challenges' } })
  }

  const saveMutation = useMutation({
    mutationFn: () => editing.value
      ? adminApi.updateCompetitionChallenge(competitionId.value, challengeId.value, payload())
      : adminApi.bindCompetitionChallenge(competitionId.value, { templateId: form.templateId, ...payload() }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
      await returnToChallenges()
    },
  })

  function removeHint(index: number) {
    if (form.hints.length === 1)
      form.hints[0] = ''
    else
      form.hints.splice(index, 1)
  }

  return {
    competitionId,
    editing,
    form,
    competition: competitionQuery.data,
    templates: templatesQuery.data,
    selectedChallenge,
    directionOptions,
    loading,
    saveMutation,
    removeHint,
    returnToChallenges,
  }
}
