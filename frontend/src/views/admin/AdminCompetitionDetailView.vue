<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
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
import { ArrowLeft, Loader2, Plus, Save, Trash2 } from 'lucide-vue-next'
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
  pointsConfig: PointsConfigDto
  difficultyCoefficient: number
  hints: ChallengeHintDto[]
}

const route = useRoute()
const router = useRouter()
const qc = useQueryClient()
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
})

const bindForm = reactive({
  templateId: '',
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'quadratic',
  difficultyCoefficient: 1,
  hints: [''],
})

const selectedEdit = reactive({
  description: '',
  initialPoints: 500,
  minimumPoints: 100,
  decayFactor: 450,
  decayFunction: 'quadratic',
  difficultyCoefficient: 1,
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
  bindForm.initialPoints = competitionForm.initialPoints
  bindForm.minimumPoints = competitionForm.minimumPoints
  bindForm.decayFactor = competitionForm.decayFactor
  bindForm.decayFunction = competitionForm.decayFunction
  bindForm.difficultyCoefficient = competitionForm.difficultyCoefficient
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
    toast.success('Competition settings saved.')
  },
  onError: () => toast.error('Failed to save competition settings.'),
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
    hints: cleanHints(bindForm.hints),
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    bindForm.description = ''
    bindForm.hints = ['']
    toast.success('Challenge deployed to competition.')
  },
  onError: () => toast.error('Failed to deploy challenge.'),
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
    hints: cleanHints(selectedEdit.hints),
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    toast.success('Competition challenge updated.')
  },
  onError: () => toast.error('Failed to update competition challenge.'),
})

const deleteChallengeMutation = useMutation({
  mutationFn: (challengeId: string) => adminApi.deleteCompetitionChallenge(competitionId.value, challengeId),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionChallenges(competitionId.value) })
    selectedChallengeId.value = null
    toast.success('Competition challenge removed.')
  },
  onError: () => toast.error('Failed to remove competition challenge.'),
})

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
          Competitions
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">{{ competition?.title ?? 'Competition' }}</h2>
          <p class="text-sm text-muted-foreground">Manage core rules, scoring defaults, and deployed challenges.</p>
        </div>
      </div>
      <Badge v-if="competition?.status" variant="outline" class="capitalize">{{ competition.status }}</Badge>
    </div>

    <div v-if="loadingCompetition" class="rounded-xl border bg-card p-8 text-center text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      Loading competition...
    </div>

    <div v-else class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
      <section class="space-y-6">
        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5 flex items-center justify-between gap-4">
            <div>
              <h3 class="font-semibold">Competition settings</h3>
              <p class="text-sm text-muted-foreground">Core timing, mode, status, scoring decay, and difficulty defaults.</p>
            </div>
            <Button :disabled="saveCompetitionMutation.isPending.value" @click="saveCompetitionMutation.mutate()">
              <Save class="mr-2 size-4" />
              Save
            </Button>
          </div>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2">
              <Label>Title</Label>
              <Input v-model="competitionForm.title" />
            </div>
            <div class="grid gap-2">
              <Label>Status</Label>
              <Select v-model="competitionForm.status">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Draft">Draft</SelectItem>
                  <SelectItem value="Published">Published</SelectItem>
                  <SelectItem value="Running">Running</SelectItem>
                  <SelectItem value="Paused">Paused</SelectItem>
                  <SelectItem value="Finished">Finished</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2 lg:col-span-2">
              <Label>Description</Label>
              <Textarea v-model="competitionForm.description" rows="3" />
            </div>
            <div class="grid gap-2">
              <Label>Game mode</Label>
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
              <Label>Difficulty coefficient</Label>
              <Input v-model.number="competitionForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
            </div>
            <div class="grid gap-2">
              <Label>Start time</Label>
              <Input v-model="competitionForm.startTime" type="datetime-local" />
            </div>
            <div class="grid gap-2">
              <Label>End time</Label>
              <Input v-model="competitionForm.endTime" type="datetime-local" />
            </div>
          </div>

          <div class="mt-6 grid gap-4 rounded-lg bg-muted/30 p-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>Initial points</Label>
              <Input v-model.number="competitionForm.initialPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Minimum points</Label>
              <Input v-model.number="competitionForm.minimumPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Decay factor</Label>
              <Input v-model.number="competitionForm.decayFactor" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Decay function</Label>
              <Select v-model="competitionForm.decayFunction">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="quadratic">Quadratic</SelectItem>
                  <SelectItem value="logarithmic">Logarithmic</SelectItem>
                  <SelectItem value="linear">Linear</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>

        <div class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-5">
            <h3 class="font-semibold">Deploy from challenge bank</h3>
            <p class="text-sm text-muted-foreground">Select a reusable template, then bind competition-specific scoring, Markdown description, and hints.</p>
          </div>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2">
              <Label>Challenge template</Label>
              <Select v-model="bindForm.templateId">
                <SelectTrigger><SelectValue placeholder="Select a template" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="template in templates" :key="template.id" :value="template.id">
                    {{ template.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>Difficulty coefficient</Label>
              <Input v-model.number="bindForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
            </div>
            <div class="grid gap-2 lg:col-span-2">
              <Label>Competition description (Markdown)</Label>
              <Textarea v-model="bindForm.description" class="font-mono text-xs" rows="5" placeholder="Leave empty to use the template description." />
            </div>
          </div>

          <div class="mt-4 grid gap-4 rounded-lg bg-muted/30 p-4 lg:grid-cols-4">
            <div class="grid gap-2">
              <Label>Initial</Label>
              <Input v-model.number="bindForm.initialPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Minimum</Label>
              <Input v-model.number="bindForm.minimumPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Decay factor</Label>
              <Input v-model.number="bindForm.decayFactor" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>Decay function</Label>
              <Select v-model="bindForm.decayFunction">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="quadratic">Quadratic</SelectItem>
                  <SelectItem value="logarithmic">Logarithmic</SelectItem>
                  <SelectItem value="linear">Linear</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div class="mt-4 space-y-2">
            <div class="flex items-center justify-between">
              <Label>Hints</Label>
              <Button variant="outline" size="sm" @click="addBindHint">
                <Plus class="mr-2 size-4" />
                Add hint
              </Button>
            </div>
            <Input v-for="(_, index) in bindForm.hints" :key="index" v-model="bindForm.hints[index]" :placeholder="`Hint ${index + 1}`" />
          </div>

          <div class="mt-5 flex justify-end">
            <Button :disabled="bindMutation.isPending.value || !bindForm.templateId" @click="bindMutation.mutate()">
              <Loader2 v-if="bindMutation.isPending.value" class="mr-2 size-4 animate-spin" />
              Deploy challenge
            </Button>
          </div>
        </div>
      </section>

      <aside class="space-y-6">
        <div class="rounded-xl border bg-card shadow-sm">
          <div class="border-b p-4">
            <h3 class="font-semibold">Competition challenges</h3>
            <p class="text-sm text-muted-foreground">Click a row to edit its deployed settings.</p>
          </div>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Title</TableHead>
                <TableHead>Points</TableHead>
                <TableHead class="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-if="loadingChallenges">
                <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                  <Loader2 class="mr-2 inline size-4 animate-spin" />
                  Loading...
                </TableCell>
              </TableRow>
              <TableRow v-else-if="!competitionChallenges?.length">
                <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                  No deployed challenges.
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
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div v-if="selectedChallenge" class="rounded-xl border bg-card p-5 shadow-sm">
          <div class="mb-4">
            <h3 class="font-semibold">{{ selectedChallenge.title }}</h3>
            <p class="text-sm text-muted-foreground">Competition-specific description, scoring, and hints.</p>
          </div>

          <div class="space-y-4">
            <div class="grid gap-2">
              <Label>Markdown description</Label>
              <Textarea v-model="selectedEdit.description" class="font-mono text-xs" rows="7" />
            </div>
            <div class="grid grid-cols-2 gap-3">
              <div class="grid gap-2">
                <Label>Initial</Label>
                <Input v-model.number="selectedEdit.initialPoints" type="number" />
              </div>
              <div class="grid gap-2">
                <Label>Minimum</Label>
                <Input v-model.number="selectedEdit.minimumPoints" type="number" />
              </div>
              <div class="grid gap-2">
                <Label>Decay factor</Label>
                <Input v-model.number="selectedEdit.decayFactor" type="number" />
              </div>
              <div class="grid gap-2">
                <Label>Difficulty</Label>
                <Input v-model.number="selectedEdit.difficultyCoefficient" type="number" min="0.1" step="0.1" />
              </div>
            </div>
            <div class="grid gap-2">
              <Label>Decay function</Label>
              <Select v-model="selectedEdit.decayFunction">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="quadratic">Quadratic</SelectItem>
                  <SelectItem value="logarithmic">Logarithmic</SelectItem>
                  <SelectItem value="linear">Linear</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="space-y-2">
              <div class="flex items-center justify-between">
                <Label>Hints</Label>
                <Button variant="outline" size="sm" @click="addEditHint">
                  <Plus class="mr-2 size-4" />
                  Add
                </Button>
              </div>
              <Input v-for="(_, index) in selectedEdit.hints" :key="index" v-model="selectedEdit.hints[index]" :placeholder="`Hint ${index + 1}`" />
            </div>
            <Button class="w-full" :disabled="updateChallengeMutation.isPending.value" @click="updateChallengeMutation.mutate()">
              <Loader2 v-if="updateChallengeMutation.isPending.value" class="mr-2 size-4 animate-spin" />
              Save deployed challenge
            </Button>
          </div>
        </div>
      </aside>
    </div>
  </div>
</template>
