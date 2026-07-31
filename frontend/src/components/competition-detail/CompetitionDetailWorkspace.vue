<script setup lang="ts">
import type { PublicChallenge } from '@/api/challengePresentation'
import type { NoCtfApplicationScoringLeaderboardLeaderboardResponse } from '@/api/generated/types.gen'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowRight, Calendar, CheckCircle2, Clock, EyeOff, Loader2, Lock, Puzzle, Trophy, UserPlus, Users } from 'lucide-vue-next'
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import { challengeApi, competitionApi, submissionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { solvedCompetitionChallengeIds } from '@/api/submissionPresentation'
import { canManagePlatformResources } from '@/api/userRole'
import ChallengeModal from '@/components/game/ChallengeModal.vue'
import { asLeaderboardSnapshot } from '@/components/game/leaderboardPresentation'
import ScoreboardView from '@/components/game/ScoreboardView.vue'
import StatTile from '@/components/layout/StatTile.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { challengeDirections, normalizeDirection } from '@/lib/challengeDirections'
import { challengeTypeLabel } from '@/lib/challengeLabels'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const competitionId = computed(() => route.params.id as string)

interface PatchSubmissionStatus {
  id?: string
  challengeId: string
  status: string | number
  fixStatus?: string | number
  attemptNumber?: number
  fileName?: string
  fixEntry?: string
  submittedAt?: string
  validatedAt?: string | null
  validationDetail?: string | null
}

interface CompetitionViewResult<T> {
  viewKey: string
  data: T
}

interface AwdpRoundState {
  roundNumber: number
  status: string
  startTime: string
  endTime?: string | null
}

interface AwdpChallengeState {
  challengeId: string
  instanceStatus: string
  breakStatus: string
  fixStatus: string
  serviceStatus: string
  currentRoundAttackScore: number
  currentRoundDefenseScore: number
  attackScorePerRound: number
  defenseScorePerRound: number
  attackAttempts: number
  defenseAttempts: number
  maxAttackAttempts: number
  maxDefenseAttempts: number
  remainingAttackAttempts: number
  remainingDefenseAttempts: number
  canSubmitFlag: boolean
  canRequestDefense: boolean
  allowAttackAfterBreakSuccess: boolean
  allowDefenseAfterFixSuccess: boolean
  fixEntry: string
  lastValidationDetail?: string | null
  cooldownUntil?: string | null
}

interface AwdpStateData {
  code: string
  competitionId: string
  teamId?: string
  currentRound?: AwdpRoundState | null
  serverTime: string
  challenges: AwdpChallengeState[]
}

const { data: competition, isLoading: loadingComp } = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: challenges, isLoading: loadingChallenges } = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => challengeApi.list(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: currentTeam, isLoading: loadingMyTeam } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeam(competitionId.value)),
  queryFn: () => teamApi.getMy(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const approvedTeam = computed(() =>
  currentTeam.value?.registrationStatus === 'approved' ? currentTeam.value : null,
)

const { data: submissions } = useQuery({
  queryKey: computed(() => queryKeys.submissions(competitionId.value)),
  queryFn: ({ signal }) => submissionApi.listAll(competitionId.value, signal),
  enabled: computed(() => Boolean(competitionId.value && approvedTeam.value)),
})

const solvedIds = computed(() =>
  solvedCompetitionChallengeIds(submissions.value ?? []),
)

const effectiveChallenges = computed<PublicChallenge[]>(() => {
  return challenges.value ?? []
})

// UI State
const modalOpen = ref(false)
const selectedChallenge = ref<PublicChallenge | null>(null)
const ui = reactive({
  activeTab: 'challenges' as 'challenges' | 'scoreboard',
})
const activeDirection = ref('ALL')
const hideSolved = ref(false)
const instanceChallengeIds = ref(new Set<string>())
const defenseChallengeIds = ref(new Set<string>())

function openChallenge(challenge: PublicChallenge) {
  selectedChallenge.value = challenge
  modalOpen.value = true
}

function onChallengeSolved() {
  queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) })
  queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) })
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function markInstanceCreated(challengeId: string) {
  instanceChallengeIds.value = new Set(instanceChallengeIds.value).add(challengeId)
}

function markDefenseRequested(challengeId: string) {
  defenseChallengeIds.value = new Set(defenseChallengeIds.value).add(challengeId)
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'active' || s === 'running')
    return 'default'
  if (s === 'upcoming' || s === 'pending')
    return 'secondary'
  if (s === 'ended' || s === 'finished')
    return 'outline'
  return 'secondary'
}

function formatShortDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

const isLoading = computed(() => loadingComp.value || loadingChallenges.value)
const isAwdMode = computed(() => competition.value?.mode === 'awd')
const isAwdpMode = computed(() => competition.value?.mode === 'awdp')
const isKohMode = computed(() => competition.value?.mode === 'koh')
const canManageCompetition = computed(() => canManagePlatformResources(auth.userRole))
const canUseParticipantActions = computed(() => Boolean(approvedTeam.value))
const canAccessChallenges = computed(() => canManageCompetition.value || Boolean(approvedTeam.value))
const canDownloadAttachments = computed(() =>
  canUseParticipantActions.value && competition.value?.status === 'running',
)

const { data: selectedChallengeDetail } = useQuery({
  queryKey: computed(() => queryKeys.challenge(
    competitionId.value,
    selectedChallenge.value?.id ?? '',
  )),
  queryFn: () => challengeApi.get(
    competitionId.value,
    selectedChallenge.value!.id,
  ),
  enabled: computed(() =>
    modalOpen.value
    && Boolean(competitionId.value)
    && Boolean(selectedChallenge.value?.id),
  ),
})
const activeChallengeDetail = computed(() =>
  selectedChallengeDetail.value?.id === selectedChallenge.value?.id
    ? selectedChallengeDetail.value
    : null,
)

const { data: headerLeaderboard } = useQuery<
  NoCtfApplicationScoringLeaderboardLeaderboardResponse | null
>({
  queryKey: computed(() => [...queryKeys.leaderboard(competitionId.value), 'header-score']),
  queryFn: async () =>
    asLeaderboardSnapshot(await competitionApi.leaderboard(competitionId.value)) ?? null,
  enabled: computed(() => !!competitionId.value && !!approvedTeam.value?.id),
  refetchInterval: computed(() => approvedTeam.value?.id ? 10_000 : false),
})

watch(
  () => [competitionId.value, approvedTeam.value?.id, currentTeam.value?.id, headerLeaderboard.value?.entries],
  () => {
    if (!competitionId.value) {
      scoreStore.reset()
      return
    }

    if (!approvedTeam.value) {
      scoreStore.setCurrentTeamScore(
        competitionId.value,
        currentTeam.value?.id ?? null,
        currentTeam.value?.name ?? auth.user?.userName ?? null,
        null,
      )
      return
    }

    const entries = headerLeaderboard.value?.entries ?? []
    const currentEntry = entries.find(entry => entry.teamId === approvedTeam.value?.id)
    if (!currentEntry) {
      scoreStore.setCurrentTeamScore(competitionId.value, approvedTeam.value.id, approvedTeam.value.name, 0)
      return
    }

    scoreStore.updateFromLeaderboard(entries, approvedTeam.value.id, competitionId.value)
  },
  { immediate: true },
)

const { data: patchSubmissions } = useQuery({
  queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
  queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatus[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value && isAwdpMode.value),
  refetchInterval: computed(() => isAwdpMode.value ? 10_000 : false),
})

const { data: awdpStateView } = useQuery({
  queryKey: computed(() => queryKeys.awdpState(competitionId.value)),
  queryFn: () => competitionApi.view<CompetitionViewResult<AwdpStateData>>(competitionId.value, 'challenge-state'),
  enabled: computed(() => !!competitionId.value && isAwdpMode.value && !!approvedTeam.value?.id),
  refetchInterval: computed(() => isAwdpMode.value ? 5_000 : false),
})

const awdpStateData = computed(() => awdpStateView.value?.data ?? null)

const filteredChallenges = computed(() => {
  return effectiveChallenges.value.filter((challenge) => {
    const solved = solvedIds.value.has(challenge.id)
    const matchesDirection = activeDirection.value === 'ALL' || normalizeDirection(challenge.direction) === activeDirection.value
    const matchesSolved = !hideSolved.value || !solved
    return matchesDirection && matchesSolved
  })
})

const boardRef = ref<HTMLElement | null>(null)
const cardRefs = ref<Record<string, HTMLElement | null>>({})
interface ConnectionLine {
  x1: number
  y1: number
  x2: number
  y2: number
  className: string
}
const connectionLines = ref<ConnectionLine[]>([])

async function recalcConnectionLines() {
  await nextTick()
  const container = boardRef.value
  if (!container)
    return
  const containerRect = container.getBoundingClientRect()
  const groups = new Map<string, PublicChallenge[]>()
  for (const challenge of filteredChallenges.value) {
    const dir = normalizeDirection(challenge.direction)
    if (!groups.has(dir))
      groups.set(dir, [])
    groups.get(dir)!.push(challenge)
  }
  const lines: ConnectionLine[] = []
  for (const [dir, list] of groups) {
    for (let i = 0; i < list.length - 1; i++) {
      const a = cardRefs.value[list[i].id]
      const b = cardRefs.value[list[i + 1].id]
      if (!a || !b)
        continue
      const rectA = a.getBoundingClientRect()
      const rectB = b.getBoundingClientRect()
      lines.push({
        x1: rectA.left + rectA.width / 2 - containerRect.left,
        y1: rectA.top - containerRect.top + 6,
        x2: rectB.left + rectB.width / 2 - containerRect.left,
        y2: rectB.top - containerRect.top + 6,
        className: challengeTypeLabel(dir).lineClassName,
      })
    }
  }
  connectionLines.value = lines
}

let resizeTimeout: ReturnType<typeof setTimeout> | null = null
function onBoardResize() {
  if (resizeTimeout)
    clearTimeout(resizeTimeout)
  resizeTimeout = setTimeout(() => recalcConnectionLines(), 100)
}

watch(filteredChallenges, () => {
  recalcConnectionLines()
  if (resizeTimeout)
    clearTimeout(resizeTimeout)
  resizeTimeout = setTimeout(() => recalcConnectionLines(), 350)
}, { immediate: true })

onMounted(() => {
  window.addEventListener('resize', onBoardResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', onBoardResize)
  if (resizeTimeout)
    clearTimeout(resizeTimeout)
})

const directionOptions = computed(() => {
  const counts = new Map<string, { total: number, unsolved: number }>()
  for (const dir of challengeDirections)
    counts.set(dir, { total: 0, unsolved: 0 })

  for (const challenge of effectiveChallenges.value) {
    const direction = normalizeDirection(challenge.direction)
    const current = counts.get(direction) ?? { total: 0, unsolved: 0 }
    current.total += 1
    if (!solvedIds.value.has(challenge.id))
      current.unsolved += 1
    counts.set(direction, current)
  }

  const entries = challengeDirections
    .filter(dir => (counts.get(dir)?.total ?? 0) > 0)
    .map(dir => ({ direction: dir, ...(counts.get(dir) ?? { total: 0, unsolved: 0 }) }))
    .sort((a, b) => a.direction.localeCompare(b.direction))

  return [
    {
      direction: 'ALL',
      total: effectiveChallenges.value.length,
      unsolved: effectiveChallenges.value.filter(challenge => !solvedIds.value.has(challenge.id)).length,
    },
    ...entries,
  ]
})

const selectedChallengePatchSubmissions = computed(() => {
  if (!selectedChallenge.value)
    return []
  return (patchSubmissions.value ?? []).filter(item => item.challengeId === selectedChallenge.value?.id)
})

const selectedChallengeAwdpState = computed(() => {
  if (!selectedChallenge.value)
    return null
  return awdpStateData.value?.challenges.find(item => item.challengeId === selectedChallenge.value?.id) ?? null
})

const now = ref(new Date())
let nowTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  nowTimer = setInterval(() => {
    now.value = new Date()
  }, 1000)
})

onUnmounted(() => {
  if (nowTimer)
    clearInterval(nowTimer)
})

const timeRemaining = computed(() => {
  if (!competition.value)
    return '-'
  const end = new Date(competition.value.endTime).getTime()
  const diff = end - now.value.getTime()
  if (diff <= 0)
    return t('competitions.status.ended')
  const hours = Math.floor(diff / 3600000)
  const minutes = Math.floor((diff % 3600000) / 60000)
  const seconds = Math.floor((diff % 60000) / 1000)
  return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})

function handleInstanceCreated(challengeId: string) {
  markInstanceCreated(challengeId)
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function handlePatchUploaded() {
  queryClient.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function rotationClass(id: string) {
  let hash = 0
  for (let i = 0; i < id.length; i++)
    hash = id.charCodeAt(i) + ((hash << 5) - hash)
  const rotations = ['-rotate-2', '-rotate-1', 'rotate-0', 'rotate-1', 'rotate-2']
  return rotations[Math.abs(hash) % rotations.length]
}
</script>

<template>
  <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6">
    <!-- Header Section -->
    <div v-if="isLoading" class="space-y-4">
      <Skeleton class="h-10 w-1/3" />
      <Skeleton class="h-6 w-1/2" />
    </div>
    <Panel v-else-if="competition" class="space-y-6 p-5 md:p-6">
      <div class="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">
            {{ competition.title }}
          </h1>
          <p v-if="competition.description" class="mt-1 max-w-3xl text-sm text-muted-foreground">
            {{ competition.description }}
          </p>
          <div class="mt-3 flex flex-wrap items-center gap-2">
            <Badge :variant="statusVariant(competition.status)">
              {{ competition.status }}
            </Badge>
            <Badge variant="outline" class="border-border bg-transparent text-foreground">
              {{ competition.mode.toUpperCase() }}
            </Badge>
          </div>
        </div>
        <Button
          v-if="isAwdpMode"
          variant="outline"
          size="sm"
          as-child
          class="shrink-0 border-border bg-secondary/50 text-foreground hover:bg-secondary"
        >
          <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
            {{ t('awdp.screenEntry') }}
            <ArrowRight class="size-4" />
          </RouterLink>
        </Button>
      </div>

      <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatTile
          :label="t('common.score')"
          :value="scoreStore.myTeamScore ?? '-'"
          :description="t('common.team')"
          :icon="Trophy"
          tone="warning"
        />
        <StatTile
          :label="t('challenges.solved')"
          :value="`${solvedIds.size} / ${effectiveChallenges.length || 0}`"
          :description="t('common.challenge')"
          :icon="CheckCircle2"
          tone="success"
        />
        <StatTile
          :label="t('common.timeRemaining')"
          :value="timeRemaining"
          :icon="Clock"
        />
        <StatTile
          :label="t('common.date')"
          :value="`${formatShortDate(competition.startTime)} ~ ${formatShortDate(competition.endTime)}`"
          :icon="Calendar"
        />
      </div>
    </Panel>

    <!-- Registration Card -->
    <Card>
      <Panel class="p-4">
        <div class="mb-4 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h3 class="flex items-center gap-2 font-semibold">
              <Users class="size-4 text-primary" />
              {{ t('teams.currentRegistration') }}
            </h3>
            <p class="text-sm text-muted-foreground">
              {{ t('teams.currentRegistrationDescription') }}
            </p>
          </div>
          <Badge
            v-if="currentTeam"
            :variant="currentTeam.registrationStatus === 'approved' ? 'default' : currentTeam.registrationStatus === 'rejected' ? 'destructive' : 'secondary'"
          >
            {{ currentTeam.registrationStatus }}
          </Badge>
        </div>

        <div v-if="loadingMyTeam" class="text-sm text-muted-foreground">
          <Loader2 class="mr-2 inline size-4 animate-spin" />
          {{ t('common.loading') }}
        </div>
        <div v-else-if="currentTeam" class="grid gap-3 md:grid-cols-[1fr_auto] md:items-center">
          <div class="space-y-1">
            <div class="font-medium">
              {{ currentTeam.name }}
            </div>
            <div class="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
              <span v-if="competition">{{ currentTeam.memberCount }} / {{ competition.maxTeamMembers }} {{ t('common.members') }}</span>
              <span class="flex items-center gap-1">
                <Lock class="size-3" />
                {{ currentTeam.isLocked ? t('teams.locked') : t('teams.unlocked') }}
              </span>
            </div>
          </div>
          <div v-if="!canAccessChallenges" class="space-y-2 rounded-lg border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
            <div>{{ currentTeam.registrationStatus === 'rejected' ? t('teams.rejectedDetail') : t('teams.waitingApproval') }}</div>
            <Button variant="outline" size="sm" as-child>
              <RouterLink :to="`/competitions/${competitionId}/register`">
                {{ t('teams.manageRegistration') }}
              </RouterLink>
            </Button>
          </div>
          <Button v-else as-child>
            <RouterLink :to="`/competitions/${competitionId}/register`">
              {{ t('teams.manageRegistration') }}
              <ArrowRight class="size-4" />
            </RouterLink>
          </Button>
        </div>
        <div v-else class="grid gap-3 md:grid-cols-[1fr_auto] md:items-center">
          <div class="rounded-lg border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
            {{ t('teams.notRegisteredDetail') }}
          </div>
          <Button as-child>
            <RouterLink :to="`/competitions/${competitionId}/register`">
              <UserPlus class="size-4" />
              {{ t('teams.registerForCompetition') }}
            </RouterLink>
          </Button>
        </div>
      </Panel>
    </Card>

    <!-- Main Content Tabs -->
    <Tabs v-model="ui.activeTab" class="w-full">
      <TabsList class="mb-8 w-full max-w-md grid grid-cols-2">
        <TabsTrigger value="challenges">
          <Puzzle class="size-4" />
          {{ t('challenges.title') }}
        </TabsTrigger>
        <TabsTrigger value="scoreboard">
          <Trophy class="size-4" />
          {{ t('scoreboard.title') }}
        </TabsTrigger>
      </TabsList>

      <TabsContent value="challenges" class="mt-0">
        <Card v-if="!canAccessChallenges" class="flex min-h-40 flex-col items-center justify-center border-dashed px-4 py-8 text-center text-muted-foreground">
          {{ t('teams.challengeLocked') }}
        </Card>
        <div v-else class="grid grid-cols-1 gap-6 lg:grid-cols-[240px_minmax(0,1fr)]">
          <!-- Direction filter sidebar -->
          <Card class="p-3 lg:sticky lg:top-24 lg:self-start">
            <div class="mb-3 px-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">
              {{ t('challenges.directions') }}
            </div>
            <div class="space-y-2">
              <Button
                v-for="item in directionOptions"
                :key="item.direction"
                :variant="activeDirection === item.direction ? 'default' : 'outline'"
                class="w-full justify-between"
                @click="activeDirection = item.direction"
              >
                <span>{{ item.direction === 'ALL' ? t('common.all') : item.direction }}</span>
                <Badge :variant="activeDirection === item.direction ? 'secondary' : 'outline'">
                  {{ hideSolved ? item.unsolved : item.total }}
                </Badge>
              </Button>
            </div>

            <Separator class="my-3" />

            <Button
              :variant="hideSolved ? 'default' : 'outline'"
              class="w-full justify-between"
              @click="hideSolved = !hideSolved"
            >
              <span class="flex items-center gap-2">
                <EyeOff class="size-4" />
                {{ t('challenges.hideSolved') }}
              </span>
              <span class="text-xs tabular-nums">{{ hideSolved ? t('common.yes') : t('common.no') }}</span>
            </Button>
          </Card>

          <!-- Challenge list -->
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-semibold">
                {{ t('challenges.workspaceTitle') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('challenges.workspaceSubtitle', { count: filteredChallenges.length }) }}
              </p>
            </div>

            <div v-if="isLoading" class="grid grid-cols-1 gap-6 rounded-lg bg-muted/30 p-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              <Skeleton v-for="i in 6" :key="i" class="aspect-[3/4] w-full rounded-sm" />
            </div>

            <Card v-else-if="filteredChallenges.length === 0" class="flex min-h-40 flex-col items-center justify-center border-dashed px-4 py-20 text-center">
              <div class="mb-4 flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
                <Puzzle class="size-6" />
              </div>
              <h3 class="text-lg font-medium">
                {{ t('challenges.empty') }}
              </h3>
              <p class="mt-1 text-sm text-muted-foreground">
                {{ t('challenges.emptyDetail') }}
              </p>
            </Card>

            <div
              v-else
              ref="boardRef"
              v-auto-animate
              class="relative grid grid-cols-1 gap-6 rounded-lg bg-muted/30 p-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4"
            >
              <svg
                class="pointer-events-none absolute inset-0 z-0 overflow-visible"
                :width="boardRef?.clientWidth"
                :height="boardRef?.clientHeight"
              >
                <line
                  v-for="(line, idx) in connectionLines"
                  :key="idx"
                  :x1="line.x1"
                  :y1="line.y1"
                  :x2="line.x2"
                  :y2="line.y2"
                  :class="['stroke-2 opacity-60', line.className]"
                />
              </svg>

              <div
                v-for="challenge in filteredChallenges"
                :key="challenge.id"
                :ref="(el) => { if (el) cardRefs[challenge.id] = el as HTMLElement }"
                class="group relative z-10 cursor-pointer transition-all hover:-translate-y-1 hover:shadow-float"
                :class="[rotationClass(challenge.id), solvedIds.has(challenge.id) && 'opacity-80']"
                role="button"
                tabindex="0"
                @click="openChallenge(challenge)"
                @keydown.enter.prevent="openChallenge(challenge)"
                @keydown.space.prevent="openChallenge(challenge)"
              >
                <Card class="relative overflow-visible rounded-sm bg-background shadow-md">
                <!-- Pin -->
                <div
                  class="absolute -top-1.5 left-1/2 z-20 size-3 -translate-x-1/2 rounded-full border-2 border-background shadow-sm"
                  :class="challengeTypeLabel(challenge.direction).pinClassName"
                />

                <!-- Photo area -->
                <CardContent class="aspect-[4/3] bg-muted/80 p-3">
                  <Panel variant="default" class="relative flex h-full flex-col justify-between p-3">
                    <h4 class="line-clamp-2 text-sm font-bold leading-tight">
                      {{ challenge.title }}
                    </h4>

                    <div class="flex flex-1 flex-col items-center justify-center py-2">
                      <span class="text-3xl font-black tabular-nums text-foreground">
                        {{ challenge.baseScore }}
                      </span>
                    </div>

                    <span
                      class="pointer-events-none absolute bottom-1 right-2 select-none text-2xl font-black uppercase tracking-[0.15em] text-foreground/30 rotate-[-12deg]"
                    >
                      {{ normalizeDirection(challenge.direction) }}
                    </span>
                  </Panel>
                </CardContent>

                <!-- Caption -->
                <div class="px-4 pb-2 pt-1">
                  <p class="line-clamp-2 text-xs text-muted-foreground">
                    {{ challenge.description || t('challenges.noDescription') }}
                  </p>
                </div>

                <!-- Solved stamp overlay (PNG sticker above the card) -->
                <img
                  v-if="solvedIds.has(challenge.id)"
                  src="/stamps/solved.webp"
                  alt=""
                  aria-hidden="true"
                  draggable="false"
                  class="pointer-events-none absolute left-1/2 top-1/2 z-40 w-[120%] max-w-none -translate-x-1/2 -translate-y-1/2 rotate-[-8deg] select-none transition-transform duration-200 group-hover:rotate-[-4deg]"
                >

                <!-- Footer action -->
                <div class="border-t border-border px-4 py-2">
                  <Button size="sm" variant="ghost" class="w-full" @click.stop="openChallenge(challenge)">
                    {{ t('common.start') }}
                  </Button>
                </div>
                </Card>
              </div>
            </div>
          </div>
        </div>
      </TabsContent>

      <TabsContent value="scoreboard" class="mt-0">
        <Card>
          <CardHeader>
            <CardTitle>{{ t('scoreboard.fullBoard') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <ScoreboardView :competition-id="competitionId" :team-id="approvedTeam?.id" full />
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>

    <!-- Challenge Modal -->
    <ChallengeModal
      v-if="selectedChallenge"
      v-model:open="modalOpen"
      :challenge="activeChallengeDetail ?? selectedChallenge"
      :competition-id="competitionId"
      :solved="solvedIds.has(selectedChallenge.id)"
      :game-mode-type="competition?.mode ?? 'ctf'"
      :is-awd-mode="isAwdMode"
      :is-awdp-mode="isAwdpMode"
      :instance-ready="instanceChallengeIds.has(selectedChallenge.id)"
      :defense-enabled="defenseChallengeIds.has(selectedChallenge.id)"
      :patch-submissions="selectedChallengePatchSubmissions"
      :awdp-state="selectedChallengeAwdpState"
      :awdp-current-round="awdpStateData?.currentRound ?? null"
      :can-create-instance="canUseParticipantActions"
      :can-submit-flag="canUseParticipantActions && !isKohMode && (!isAwdpMode || selectedChallengeAwdpState?.canSubmitFlag !== false)"
      :can-request-defense="canUseParticipantActions && (!isAwdpMode || selectedChallengeAwdpState?.canRequestDefense !== false)"
      :can-download-attachments="canDownloadAttachments"
      :has-challenge-detail="Boolean(activeChallengeDetail)"
      @create-instance="handleInstanceCreated(selectedChallenge.id)"
      @request-defense="markDefenseRequested(selectedChallenge.id)"
      @patch-uploaded="handlePatchUploaded"
      @solved="onChallengeSolved"
    />
  </div>
</template>
