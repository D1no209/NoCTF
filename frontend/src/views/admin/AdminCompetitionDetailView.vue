<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
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
import { ArrowLeft, Check, Loader2, Lock, Plus, Save, ShieldAlert, Trash2, Unlock, X } from 'lucide-vue-next'
import { toast } from 'vue-sonner'

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
  teamRegistrationAutoApprove: boolean
  maxTeamMembers: number
  tracksEnabled: boolean
  trackNames: string[]
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
  decayFunction: 'quadratic',
  difficultyCoefficient: 1,
  teamRegistrationAutoApprove: true,
  maxTeamMembers: 5,
  tracksEnabled: false,
  trackNamesText: '',
})

const bindForm = reactive({
  templateId: '',
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'quadratic',
  difficultyCoefficient: 1,
  flagPrefix: 'flag',
  hints: [''],
})

const selectedEdit = reactive({
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'quadratic',
  difficultyCoefficient: 1,
  flagPrefix: 'flag',
  hints: [''],
})

function toDateTimeLocal(value: string) {
  if (!value) return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
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
      initialPoints: Number(competitionForm.initialPoints) || 500,
      minimumPoints: Number(competitionForm.minimumPoints) || 100,
      decayFactor: Number(competitionForm.decayFactor) || 450,
      decayFunction: competitionForm.decayFunction || 'quadratic',
    },
    difficultyCoefficient: Number(competitionForm.difficultyCoefficient) || 1,
    teamRegistrationAutoApprove: competitionForm.teamRegistrationAutoApprove,
    maxTeamMembers: Number(competitionForm.maxTeamMembers) || 5,
    tracksEnabled: competitionForm.tracksEnabled,
    trackNames: competitionForm.trackNamesText.split(/\r?\n|,/).map(item => item.trim()).filter(Boolean),
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
  competitionForm.decayFunction = value.defaultPointsConfig?.decayFunction ?? 'quadratic'
  competitionForm.difficultyCoefficient = value.difficultyCoefficient ?? 1
  competitionForm.teamRegistrationAutoApprove = value.teamRegistrationAutoApprove ?? true
  competitionForm.maxTeamMembers = value.maxTeamMembers ?? 5
  competitionForm.tracksEnabled = value.tracksEnabled ?? false
  competitionForm.trackNamesText = (value.trackNames ?? []).join('\n')
  bindForm.initialPoints = competitionForm.initialPoints
  bindForm.minimumPoints = competitionForm.minimumPoints
  bindForm.decayFactor = competitionForm.decayFactor
  bindForm.decayFunction = competitionForm.decayFunction
  bindForm.difficultyCoefficient = competitionForm.difficultyCoefficient
  bindForm.flagPrefix = 'flag'
}, { immediate: true })

watch(templates, (items) => {
  if (!bindForm.templateId && items?.length) bindForm.templateId = items[0].id
}, { immediate: true })

const selectedChallenge = computed(() => competitionChallenges.value?.find(c => c.id === selectedChallengeId.value) ?? null)

watch(selectedChallenge, (challenge) => {
  if (!challenge) return
  selectedEdit.description = challenge.description ?? ''
  selectedEdit.initialPoints = challenge.pointsConfig?.initialPoints ?? 500
  selectedEdit.minimumPoints = challenge.pointsConfig?.minimumPoints ?? 100
  selectedEdit.decayFactor = challenge.pointsConfig?.decayFactor ?? 450
  selectedEdit.decayFunction = challenge.pointsConfig?.decayFunction ?? 'quadratic'
  selectedEdit.difficultyCoefficient = challenge.difficultyCoefficient ?? 1
  selectedEdit.flagPrefix = challenge.flagPrefix ?? 'flag'
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
      initialPoints: Number(bindForm.initialPoints) || 500,
      minimumPoints: Number(bindForm.minimumPoints) || 100,
      decayFactor: Number(bindForm.decayFactor) || 450,
      decayFunction: bindForm.decayFunction || 'quadratic',
    },
    difficultyCoefficient: Number(bindForm.difficultyCoefficient) || 1,
    flagPrefix: bindForm.flagPrefix.trim() || 'flag',
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
      initialPoints: Number(selectedEdit.initialPoints) || 500,
      minimumPoints: Number(selectedEdit.minimumPoints) || 100,
      decayFactor: Number(selectedEdit.decayFactor) || 450,
      decayFunction: selectedEdit.decayFunction || 'quadratic',
    },
    difficultyCoefficient: Number(selectedEdit.difficultyCoefficient) || 1,
    flagPrefix: selectedEdit.flagPrefix.trim() || 'flag',
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
  mutationFn: ({ teamId, isLocked }: { teamId: string; isLocked: boolean }) =>
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
          <h2 class="text-2xl font-bold tracking-tight">{{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}</h2>
          <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.subtitle') }}</p>
        </div>
      </div>
      <Badge v-if="competition?.status" variant="outline" class="capitalize">{{ competition.status }}</Badge>
    </div>

    <div v-if="loadingCompetition" class="rounded-xl border bg-card p-8 text-center text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <div v-else class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
      <section class="space-y-6">
        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5 flex items-center justify-between gap-4">
            <div>
              <h3 class="font-semibold">{{ t('admin.competitionDetail.settingsTitle') }}</h3>
              <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.settingsDescription') }}</p>
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
                  <SelectItem value="Draft">{{ t('competitions.status.draft') }}</SelectItem>
                  <SelectItem value="Published">{{ t('competitions.status.published') }}</SelectItem>
                  <SelectItem value="Running">{{ t('competitions.status.running') }}</SelectItem>
                  <SelectItem value="Paused">{{ t('competitions.status.paused') }}</SelectItem>
                  <SelectItem value="Finished">{{ t('competitions.status.finished') }}</SelectItem>
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
                  <SelectItem value="Ctf">CTF</SelectItem>
                  <SelectItem value="Awd">AWD</SelectItem>
                  <SelectItem value="Awdp">AWDP</SelectItem>
                  <SelectItem value="Koh">KoH</SelectItem>
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
            <label class="flex items-center gap-3 rounded-lg border bg-muted/20 px-3 py-2 text-sm">
              <input v-model="competitionForm.teamRegistrationAutoApprove" type="checkbox" class="size-4" />
              <span>{{ t('admin.competitionDetail.autoApproveTeams') }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border bg-muted/20 px-3 py-2 text-sm">
              <input v-model="competitionForm.tracksEnabled" type="checkbox" class="size-4" />
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

          <div class="mt-6 grid gap-4 rounded-lg bg-muted/30 p-4 lg:grid-cols-4">
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
                  <SelectItem value="quadratic">{{ t('admin.competitionDetail.decayQuadratic') }}</SelectItem>
                  <SelectItem value="logarithmic">{{ t('admin.competitionDetail.decayLogarithmic') }}</SelectItem>
                  <SelectItem value="linear">{{ t('admin.competitionDetail.decayLinear') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>

        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5">
            <h3 class="font-semibold">{{ t('admin.competitionDetail.deployTitle') }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.deployDescription') }}</p>
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
            <div class="grid gap-2 lg:col-span-2">
              <Label>{{ t('admin.competitionDetail.markdownDescription') }}</Label>
              <Textarea v-model="bindForm.description" class="font-mono text-xs" rows="5" :placeholder="t('admin.competitionDetail.descriptionPlaceholder')" />
            </div>
          </div>

          <div class="mt-4 grid gap-4 rounded-lg bg-muted/30 p-4 lg:grid-cols-4">
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
                  <SelectItem value="quadratic">{{ t('admin.competitionDetail.decayQuadratic') }}</SelectItem>
                  <SelectItem value="logarithmic">{{ t('admin.competitionDetail.decayLogarithmic') }}</SelectItem>
                  <SelectItem value="linear">{{ t('admin.competitionDetail.decayLinear') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

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

        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5">
            <h3 class="font-semibold">{{ t('admin.competitionDetail.teamReviewTitle') }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.teamReviewDescription') }}</p>
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
                <TableHead class="text-right">{{ t('common.actions') }}</TableHead>
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
                  <div class="font-medium">{{ team.name }}</div>
                  <div class="text-xs text-muted-foreground">{{ team.captainName }}</div>
                  <code class="mt-1 block text-[10px] text-muted-foreground">{{ team.inviteToken }}</code>
                </TableCell>
                <TableCell>{{ team.memberCount }} / {{ competitionForm.maxTeamMembers }}</TableCell>
                <TableCell class="text-xs text-muted-foreground">{{ team.trackName || '-' }}</TableCell>
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

        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5">
            <h3 class="font-semibold">{{ t('admin.competitionDetail.cheatTitle') }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.cheatDescription') }}</p>
          </div>
          <div v-if="loadingCheatIncidents" class="py-8 text-center text-sm text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />
            {{ t('common.loading') }}
          </div>
          <div v-else-if="!cheatIncidents?.length" class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
            {{ t('admin.competitionDetail.noCheatIncidents') }}
          </div>
          <div v-else class="space-y-3">
            <div v-for="incident in cheatIncidents" :key="incident.id" class="rounded-lg border bg-muted/20 p-3">
              <div class="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <div class="font-medium">{{ incident.suspectTeamName }} → {{ incident.victimTeamName || '-' }}</div>
                  <div class="text-xs text-muted-foreground">{{ incident.challengeTitle }} · {{ incident.userName }} · {{ new Date(incident.createdAt).toLocaleString() }}</div>
                </div>
                <Button size="sm" variant="destructive" @click="banTeamMutation.mutate(incident.suspectTeamId)">
                  <ShieldAlert class="mr-2 size-4" />
                  {{ t('admin.competitionDetail.banTeam') }}
                </Button>
              </div>
            </div>
          </div>
        </div>

        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5">
            <h3 class="font-semibold">{{ t('admin.competitionDetail.logsTitle') }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.logsDescription') }}</p>
          </div>
          <div v-if="loadingLogs" class="py-8 text-center text-sm text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />
            {{ t('common.loading') }}
          </div>
          <div v-else class="max-h-[360px] space-y-2 overflow-y-auto pr-1">
            <div v-for="log in competitionLogs ?? []" :key="log.id" class="rounded-lg border bg-muted/20 p-3 text-sm">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <Badge :variant="log.level === 'error' ? 'destructive' : log.level === 'warning' ? 'secondary' : 'outline'">
                  {{ log.eventType }}
                </Badge>
                <span class="text-xs text-muted-foreground">{{ new Date(log.createdAt).toLocaleString() }}</span>
              </div>
              <p class="mt-2">{{ log.message }}</p>
              <p v-if="log.teamName || log.challengeTitle" class="mt-1 text-xs text-muted-foreground">
                {{ log.teamName || '-' }} · {{ log.challengeTitle || '-' }}
              </p>
            </div>
            <div v-if="!competitionLogs?.length" class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.noLogs') }}
            </div>
          </div>
        </div>
      </section>

      <aside class="space-y-6">
        <div class="rounded-xl border bg-card shadow-sm">
          <div class="border-b p-4">
            <h3 class="font-semibold">{{ t('admin.competitionDetail.competitionChallenges') }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.competitionChallengesDescription') }}</p>
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
                  <div class="font-medium">{{ challenge.title }}</div>
                  <div class="text-xs text-muted-foreground">{{ challenge.typeId }}</div>
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

        <div v-if="selectedChallenge" class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-4">
            <h3 class="font-semibold">{{ selectedChallenge.title }}</h3>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.editChallengeDescription') }}</p>
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
                  <SelectItem value="quadratic">{{ t('admin.competitionDetail.decayQuadratic') }}</SelectItem>
                  <SelectItem value="logarithmic">{{ t('admin.competitionDetail.decayLogarithmic') }}</SelectItem>
                  <SelectItem value="linear">{{ t('admin.competitionDetail.decayLinear') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
              <Input v-model="selectedEdit.flagPrefix" placeholder="flag" />
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
