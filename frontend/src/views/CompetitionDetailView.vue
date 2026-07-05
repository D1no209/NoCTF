<script setup lang="ts">
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowRight, Calendar, CheckCircle2, EyeOff, Puzzle, Trophy } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import ChallengeCard from '@/components/game/ChallengeCard.vue'
import ChallengeModal from '@/components/game/ChallengeModal.vue'
import ScoreboardView from '@/components/game/ScoreboardView.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { normalizeDirection } from '@/lib/challengeDirections'
import { competitionStatusVariant, gameModeVariant } from '@/lib/statusTones'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const competitionId = computed(() => route.params.id as string)

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  startTime: string
  endTime: string
  gameModeType: string
  maxTeamMembers: number
  teamRegistrationAutoApprove: boolean
  tracksEnabled: boolean
  trackNames: string[]
}

interface Challenge {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  deploymentType?: string | null
  description?: string | null
  descriptionFormat?: string | null
  hints?: string[]
  attachmentUrl?: string | null
  patchTemplateUrl?: string | null
  totalStageCount?: number | null
  solvedStageCount?: number | null
  totalScore?: number | null
}

interface SubmissionItem {
  challengeId: string
}

interface SolvedFlagItem {
  challengeId: string
  flagId: string
}

interface SubmissionsResponse {
  competitionId: string
  teamId: string
  solvedChallenges: SubmissionItem[]
  solvedFlags?: SolvedFlagItem[]
}

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

interface MyCompetitionTeam {
  id: string
  name: string
  inviteToken: string
  memberCount: number
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  isCaptain: boolean
}

interface LeaderboardEntry {
  teamId?: string | null
  teamName?: string | null
  totalScore?: number | null
  score?: number | null
}

interface LeaderboardResponse {
  entries?: LeaderboardEntry[]
}

const { data: competition, isLoading: loadingComp } = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get<Competition>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: challenges, isLoading: loadingChallenges } = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<Challenge[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: submissionsResponse, refetch: refetchSubmissions } = useQuery({
  queryKey: computed(() => queryKeys.submissions(competitionId.value)),
  queryFn: () => competitionApi.submissions<SubmissionsResponse>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: myTeams } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<MyCompetitionTeam[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const solvedIds = computed(
  () => new Set((submissionsResponse.value?.solvedChallenges ?? []).map((s) => s.challengeId)),
)
const solvedFlagCounts = computed(() => {
  const counts = new Map<string, number>()
  for (const flag of submissionsResponse.value?.solvedFlags ?? []) {
    counts.set(flag.challengeId, (counts.get(flag.challengeId) ?? 0) + 1)
  }
  return counts
})

// UI State
const modalOpen = ref(false)
const selectedChallenge = ref<Challenge | null>(null)
const ui = reactive({
  activeTab: 'challenges' as 'challenges' | 'scoreboard',
})
const activeDirection = ref('ALL')
const hideSolved = ref(false)
const instanceChallengeIds = ref(new Set<string>())
const defenseChallengeIds = ref(new Set<string>())

function openChallenge(challenge: Challenge) {
  selectedChallenge.value = challenge
  modalOpen.value = true
}

function onChallengeSolved() {
  refetchSubmissions()
  queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) })
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function markInstanceCreated(challengeId: string) {
  instanceChallengeIds.value = new Set(instanceChallengeIds.value).add(challengeId)
}

function markDefenseRequested(challengeId: string) {
  defenseChallengeIds.value = new Set(defenseChallengeIds.value).add(challengeId)
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const isLoading = computed(() => loadingComp.value || loadingChallenges.value)
const isAwdMode = computed(() => (competition.value?.gameModeType ?? '').toLowerCase() === 'awd')
const isAwdpMode = computed(() => (competition.value?.gameModeType ?? '').toLowerCase() === 'awdp')
const canManageCompetition = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))
const approvedTeam = computed(
  () => (myTeams.value ?? []).find((team) => team.registrationStatus === 'approved') ?? null,
)
const currentTeam = computed(() => approvedTeam.value ?? myTeams.value?.[0] ?? null)
const canUseParticipantActions = computed(() =>
  Boolean(approvedTeam.value && !approvedTeam.value.isBanned),
)
const canAccessChallenges = computed(
  () => canManageCompetition.value || Boolean(approvedTeam.value && !approvedTeam.value.isBanned),
)

const { data: headerLeaderboard } = useQuery({
  queryKey: computed(() => [...queryKeys.leaderboard(competitionId.value), 'header-score']),
  queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<LeaderboardResponse>,
  enabled: computed(() => !!competitionId.value && !!approvedTeam.value?.id),
  refetchInterval: computed(() => (approvedTeam.value?.id ? 10_000 : false)),
})

watch(
  () => [
    competitionId.value,
    approvedTeam.value?.id,
    currentTeam.value?.id,
    headerLeaderboard.value?.entries,
  ],
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
    const currentEntry = entries.find((entry) => entry.teamId === approvedTeam.value?.id)
    if (!currentEntry) {
      scoreStore.setCurrentTeamScore(
        competitionId.value,
        approvedTeam.value.id,
        approvedTeam.value.name,
        0,
      )
      return
    }

    scoreStore.updateFromLeaderboard(entries as never, approvedTeam.value.id, competitionId.value)
  },
  { immediate: true },
)

const { data: patchSubmissions } = useQuery({
  queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
  queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatus[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value && isAwdpMode.value),
  refetchInterval: computed(() => (isAwdpMode.value ? 10_000 : false)),
})

const { data: awdpStateView } = useQuery({
  queryKey: computed(() => queryKeys.awdpState(competitionId.value)),
  queryFn: () =>
    competitionApi.view<CompetitionViewResult<AwdpStateData>>(
      competitionId.value,
      'challenge-state',
    ),
  enabled: computed(() => !!competitionId.value && isAwdpMode.value && !!approvedTeam.value?.id),
  refetchInterval: computed(() => (isAwdpMode.value ? 5_000 : false)),
})

const awdpStateData = computed(() => awdpStateView.value?.data ?? null)

const filteredChallenges = computed(() => {
  return (challenges.value ?? []).filter((challenge) => {
    const solved = solvedIds.value.has(challenge.id)
    const matchesDirection =
      activeDirection.value === 'ALL' ||
      normalizeDirection(challenge.typeId) === activeDirection.value
    const matchesSolved = !hideSolved.value || !solved
    return matchesDirection && matchesSolved
  })
})

const directionCounts = computed(() => {
  const counts = new Map<string, { total: number; unsolved: number }>()
  for (const challenge of challenges.value ?? []) {
    const direction = normalizeDirection(challenge.typeId)
    const current = counts.get(direction) ?? { total: 0, unsolved: 0 }
    current.total += 1
    if (!solvedIds.value.has(challenge.id)) current.unsolved += 1
    counts.set(direction, current)
  }
  return counts
})

const directionOptions = computed(() => {
  const entries = Array.from(directionCounts.value.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([direction, counts]) => ({ direction, ...counts }))
  return [
    {
      direction: 'ALL',
      total: challenges.value?.length ?? 0,
      unsolved: (challenges.value ?? []).filter((challenge) => !solvedIds.value.has(challenge.id))
        .length,
    },
    ...entries,
  ]
})

const selectedChallengePatchSubmissions = computed(() => {
  if (!selectedChallenge.value) return []
  return (patchSubmissions.value ?? []).filter(
    (item) => item.challengeId === selectedChallenge.value?.id,
  )
})

const selectedChallengeAwdpState = computed(() => {
  if (!selectedChallenge.value) return null
  return (
    awdpStateData.value?.challenges.find(
      (item) => item.challengeId === selectedChallenge.value?.id,
    ) ?? null
  )
})

function handleInstanceCreated(challengeId: string) {
  markInstanceCreated(challengeId)
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function handlePatchUploaded() {
  queryClient.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
  queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
}

function withChallengeProgress(challenge: Challenge): Challenge {
  if (challenge.typeId.toLowerCase() !== 'penetration') return challenge
  return {
    ...challenge,
    solvedStageCount: solvedFlagCounts.value.get(challenge.id) ?? 0,
  }
}
</script>

<template>
  <div class="noctf-page-wide">
    <!-- Header Section -->
    <div v-if="isLoading" class="space-y-4">
      <Skeleton class="h-10 w-1/3" />
      <Skeleton class="h-6 w-1/2" />
    </div>
    <div
      v-else-if="competition"
      class="noctf-panel flex flex-col justify-between gap-5 rounded-lg p-4 sm:gap-6 sm:p-6 md:flex-row md:items-center"
    >
      <div class="flex min-w-0 items-start gap-3 sm:gap-5">
        <div class="noctf-logo size-12 rounded-xl sm:size-16 sm:rounded-2xl" />
        <PageHeader :title="competition.title" :description="competition.description">
          <template #actions>
            <div class="flex flex-wrap items-center gap-2">
              <Badge
                :variant="competitionStatusVariant(competition.status)"
                class="px-2.5 py-1 text-sm font-semibold"
              >
                {{ competition.status }}
              </Badge>
              <Badge
                :variant="gameModeVariant(competition.gameModeType)"
                class="px-2.5 py-1 text-sm"
              >
                {{ competition.gameModeType }}
              </Badge>
              <Button v-if="isAwdpMode" variant="outline" size="sm" as-child>
                <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
                  {{ t('awdp.screenEntry') }}
                  <ArrowRight class="size-4" />
                </RouterLink>
              </Button>
            </div>
          </template>
        </PageHeader>
      </div>

      <div class="grid gap-3 text-sm text-muted-foreground sm:grid-cols-2">
        <div class="flex min-w-0 items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 sm:px-4">
          <Calendar class="size-4 text-primary" />
          <span class="min-w-0 break-words"
            >{{ formatDate(competition.startTime) }} ~ {{ formatDate(competition.endTime) }}</span
          >
        </div>
        <div class="flex min-w-0 items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 sm:px-4">
          <CheckCircle2 class="size-4 text-success" />
          <span
            >{{ solvedIds.size }} / {{ challenges?.length || 0 }} {{ t('challenges.solved') }}</span
          >
        </div>
      </div>
    </div>

    <div v-if="currentTeam?.isBanned" class="noctf-danger-panel">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div class="font-semibold">{{ t('teams.banned') }}</div>
          <p class="text-sm">
            {{ currentTeam.bannedReason || t('teams.bannedDetail') }}
          </p>
        </div>
        <Badge variant="destructive" class="w-fit">
          {{ t('teams.banned') }}
        </Badge>
      </div>
    </div>

    <!-- Main Content Tabs -->
    <div class="w-full">
      <div class="noctf-segmented mb-8 w-full max-w-md grid-cols-2">
        <button
          type="button"
          class="noctf-segmented-button"
          :class="ui.activeTab === 'challenges' ? 'noctf-segmented-button-active' : ''"
          @click="ui.activeTab = 'challenges'"
        >
          <Puzzle class="size-4" />
          {{ t('challenges.title') }}
        </button>
        <button
          type="button"
          class="noctf-segmented-button"
          :class="ui.activeTab === 'scoreboard' ? 'noctf-segmented-button-active' : ''"
          @click="ui.activeTab = 'scoreboard'"
        >
          <Trophy class="size-4" />
          {{ t('scoreboard.title') }}
        </button>
      </div>

      <div v-if="ui.activeTab === 'challenges'" class="mt-0">
        <div v-if="!canAccessChallenges" class="noctf-state-box text-muted-foreground">
          {{ t('teams.challengeLocked') }}
        </div>
        <div v-else class="grid grid-cols-1 gap-6 lg:grid-cols-[220px_minmax(0,1fr)]">
          <aside class="noctf-surface-plain p-3 lg:sticky lg:top-24 lg:self-start">
            <div class="mb-3 px-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">
              {{ t('challenges.directions') }}
            </div>
            <div
              class="noctf-scrollbar flex gap-2 overflow-x-auto pb-1 lg:block lg:space-y-1 lg:overflow-visible lg:pb-0"
            >
              <button
                v-for="item in directionOptions"
                :key="item.direction"
                type="button"
                class="flex min-h-11 shrink-0 items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm transition-colors hover:bg-muted lg:w-full"
                :class="
                  activeDirection === item.direction
                    ? 'bg-primary text-primary-foreground hover:bg-primary'
                    : 'text-foreground'
                "
                @click="activeDirection = item.direction"
              >
                <span>{{ item.direction === 'ALL' ? t('common.all') : item.direction }}</span>
                <span class="text-xs tabular-nums opacity-80">{{
                  hideSolved ? item.unsolved : item.total
                }}</span>
              </button>
            </div>
            <button
              type="button"
              class="mt-3 flex min-h-11 w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-sm transition-colors hover:bg-muted lg:mt-4"
              :class="
                hideSolved ? 'border-primary/50 bg-primary/5 text-primary' : 'text-muted-foreground'
              "
              @click="hideSolved = !hideSolved"
            >
              <span class="flex items-center gap-2">
                <EyeOff class="size-4" />
                {{ t('challenges.hideSolved') }}
              </span>
              <span class="font-medium">{{ hideSolved ? t('common.yes') : t('common.no') }}</span>
            </button>
          </aside>

          <!-- Challenges Section -->
          <div class="space-y-6">
            <div class="flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between">
              <div>
                <h3 class="text-lg font-semibold">
                  {{ t('challenges.workspaceTitle') }}
                </h3>
                <p class="text-sm text-muted-foreground">
                  {{ t('challenges.workspaceSubtitle', { count: filteredChallenges.length }) }}
                </p>
              </div>
            </div>

            <div v-if="isLoading" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Skeleton v-for="i in 6" :key="i" class="h-32 rounded-xl" />
            </div>
            <div v-else v-auto-animate class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
              <div
                v-for="challenge in filteredChallenges"
                :key="challenge.id"
                class="cursor-pointer group"
                @click="openChallenge(challenge)"
              >
                <ChallengeCard
                  :challenge="withChallengeProgress(challenge)"
                  :solved="solvedIds.has(challenge.id)"
                  class="transition-colors duration-200 group-hover:border-primary/20 group-hover:bg-accent/35"
                />
              </div>
            </div>

            <div v-if="!isLoading && filteredChallenges.length === 0" class="noctf-state-box py-20">
              <div
                class="size-12 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4"
              >
                <Puzzle class="size-6" />
              </div>
              <h3 class="text-lg font-medium">
                {{ t('challenges.empty') }}
              </h3>
              <p class="text-sm text-muted-foreground mt-1">
                {{ t('challenges.emptyDetail') }}
              </p>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="mt-0">
        <Card class="noctf-panel">
          <CardHeader>
            <CardTitle>{{ t('scoreboard.fullBoard') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <ScoreboardView
              :key="competitionId"
              :competition-id="competitionId"
              :team-id="approvedTeam?.id"
              full
            />
          </CardContent>
        </Card>
      </div>
    </div>

    <!-- Challenge Modal -->
    <ChallengeModal
      v-if="selectedChallenge"
      v-model:open="modalOpen"
      :challenge="selectedChallenge"
      :competition-id="competitionId"
      :solved="solvedIds.has(selectedChallenge.id)"
      :game-mode-type="competition?.gameModeType ?? 'ctf'"
      :is-awd-mode="isAwdMode"
      :is-awdp-mode="isAwdpMode"
      :instance-ready="instanceChallengeIds.has(selectedChallenge.id)"
      :defense-enabled="defenseChallengeIds.has(selectedChallenge.id)"
      :patch-submissions="selectedChallengePatchSubmissions"
      :awdp-state="selectedChallengeAwdpState"
      :awdp-current-round="awdpStateData?.currentRound ?? null"
      :can-create-instance="canUseParticipantActions"
      :can-submit-flag="
        canUseParticipantActions &&
        (!isAwdpMode || selectedChallengeAwdpState?.canSubmitFlag !== false)
      "
      :can-request-defense="
        canUseParticipantActions &&
        (!isAwdpMode || selectedChallengeAwdpState?.canRequestDefense !== false)
      "
      @create-instance="handleInstanceCreated(selectedChallenge.id)"
      @request-defense="markDefenseRequested(selectedChallenge.id)"
      @patch-uploaded="handlePatchUploaded"
      @solved="onChallengeSolved"
    />
  </div>
</template>
