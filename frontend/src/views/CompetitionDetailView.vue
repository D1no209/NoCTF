<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import PageHeader from '@/components/layout/PageHeader.vue'
import ChallengeCard from '@/components/game/ChallengeCard.vue'
import ChallengeModal from '@/components/game/ChallengeModal.vue'
import ScoreboardView from '@/components/game/ScoreboardView.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Trophy, Puzzle, Calendar, CheckCircle2, EyeOff, Loader2, Lock, Users } from 'lucide-vue-next'
import { normalizeDirection } from '@/lib/challengeDirections'
import { toast } from 'vue-sonner'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
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
  description?: string | null
  descriptionFormat?: string | null
  hints?: string[]
  attachmentUrl?: string | null
}

interface SubmissionItem {
  challengeId: string
}

interface SubmissionsResponse {
  competitionId: string
  teamId: string
  solvedChallenges: SubmissionItem[]
}

interface PatchSubmissionStatus {
  id?: string
  challengeId: string
  status: string | number
  submittedAt?: string
  validatedAt?: string | null
  validationDetail?: string | null
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

const { data: myTeams, isLoading: loadingMyTeams } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<MyCompetitionTeam[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const solvedIds = computed(() => new Set((submissionsResponse.value?.solvedChallenges ?? []).map((s) => s.challengeId)))

// UI State
const modalOpen = ref(false)
const selectedChallenge = ref<Challenge | null>(null)
const activeDirection = ref('ALL')
const hideSolved = ref(false)
const newTeamName = ref('')
const selectedTrackName = ref('')
const joinToken = ref('')
const instanceChallengeIds = ref(new Set<string>())
const defenseChallengeIds = ref(new Set<string>())

function openChallenge(challenge: Challenge) {
  selectedChallenge.value = challenge
  modalOpen.value = true
}

function onChallengeSolved() {
  refetchSubmissions()
  queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) })
}

function markInstanceCreated(challengeId: string) {
  instanceChallengeIds.value = new Set(instanceChallengeIds.value).add(challengeId)
}

function markDefenseRequested(challengeId: string) {
  defenseChallengeIds.value = new Set(defenseChallengeIds.value).add(challengeId)
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'active' || s === 'running') return 'default'
  if (s === 'upcoming' || s === 'pending') return 'secondary'
  if (s === 'ended' || s === 'finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}

const isLoading = computed(() => loadingComp.value || loadingChallenges.value)
const isAwdMode = computed(() => ['awd', 'awdp'].includes((competition.value?.gameModeType ?? '').toLowerCase()))
const isAwdpMode = computed(() => (competition.value?.gameModeType ?? '').toLowerCase() === 'awdp')
const approvedTeam = computed(() => (myTeams.value ?? []).find(team => team.registrationStatus === 'approved') ?? null)
const currentTeam = computed(() => approvedTeam.value ?? myTeams.value?.[0] ?? null)
const canAccessChallenges = computed(() => Boolean(approvedTeam.value && !approvedTeam.value.isBanned))

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<MyCompetitionTeam>({
    competitionId: competitionId.value,
    name: newTeamName.value.trim(),
    trackName: competition.value?.tracksEnabled ? selectedTrackName.value : undefined,
  }),
  onSuccess: () => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<MyCompetitionTeam>(joinToken.value.trim()),
  onSuccess: () => {
    joinToken.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const { data: patchSubmissions } = useQuery({
  queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
  queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatus[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value && isAwdpMode.value),
  refetchInterval: computed(() => isAwdpMode.value ? 10_000 : false),
})

const filteredChallenges = computed(() => {
  return (challenges.value ?? []).filter((challenge) => {
    const solved = solvedIds.value.has(challenge.id)
    const matchesDirection = activeDirection.value === 'ALL' || normalizeDirection(challenge.typeId) === activeDirection.value
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
      unsolved: (challenges.value ?? []).filter(challenge => !solvedIds.value.has(challenge.id)).length,
    },
    ...entries,
  ]
})

const selectedChallengePatchSubmissions = computed(() => {
  if (!selectedChallenge.value) return []
  return (patchSubmissions.value ?? []).filter(item => item.challengeId === selectedChallenge.value?.id)
})
</script>

<template>
  <div class="mx-auto w-full max-w-[1700px] space-y-8 px-4 py-8 md:px-6">
    <!-- Header Section -->
    <div v-if="isLoading" class="space-y-4">
      <Skeleton class="h-10 w-1/3" />
      <Skeleton class="h-6 w-1/2" />
    </div>
    <div v-else-if="competition" class="noctf-panel flex flex-col justify-between gap-6 rounded-xl p-6 md:flex-row md:items-center">
      <div class="flex items-start gap-5">
        <div class="noctf-logo size-16 rounded-2xl" />
        <PageHeader
          :title="competition.title"
          :description="competition.description"
        >
          <template #actions>
            <div class="flex items-center gap-2">
              <Badge :variant="statusVariant(competition.status)" class="font-semibold">
                {{ competition.status }}
              </Badge>
              <Badge variant="secondary" class="bg-violet-100 text-violet-700">
                CTF
              </Badge>
            </div>
          </template>
        </PageHeader>
      </div>

      <div class="grid gap-3 text-sm text-muted-foreground sm:grid-cols-2">
        <div class="flex items-center gap-2 rounded-lg bg-muted/60 px-4 py-2">
          <Calendar class="size-4 text-primary" />
          <span>{{ formatDate(competition.startTime) }} ~ {{ formatDate(competition.endTime) }}</span>
        </div>
        <div class="flex items-center gap-2 rounded-lg bg-muted/60 px-4 py-2">
          <CheckCircle2 class="size-4 text-green-500" />
          <span>{{ solvedIds.size }} / {{ challenges?.length || 0 }} {{ t('challenges.solved') }}</span>
        </div>
      </div>
    </div>

    <div class="rounded-xl border bg-card p-5 shadow-sm">
      <div class="mb-4 flex items-start justify-between gap-4">
        <div>
          <h3 class="flex items-center gap-2 font-semibold">
            <Users class="size-4 text-primary" />
            {{ t('teams.registrationTitle') }}
          </h3>
          <p class="text-sm text-muted-foreground">{{ t('teams.registrationDescription') }}</p>
        </div>
        <Badge
          v-if="currentTeam"
          :variant="currentTeam.registrationStatus === 'approved' ? 'default' : currentTeam.registrationStatus === 'rejected' ? 'destructive' : 'secondary'"
        >
          {{ currentTeam.registrationStatus }}
        </Badge>
      </div>

      <div v-if="loadingMyTeams" class="text-sm text-muted-foreground">
        <Loader2 class="mr-2 inline size-4 animate-spin" />
        {{ t('common.loading') }}
      </div>
      <div v-else-if="currentTeam" class="grid gap-3 md:grid-cols-[1fr_auto] md:items-center">
        <div class="space-y-1">
          <div class="font-medium">{{ currentTeam.name }}</div>
          <div class="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
            <span>{{ currentTeam.memberCount }} / {{ competition?.maxTeamMembers ?? 5 }} {{ t('common.members') }}</span>
            <span v-if="currentTeam.trackName">{{ currentTeam.trackName }}</span>
            <span class="flex items-center gap-1">
              <Lock class="size-3" />
              {{ currentTeam.isLocked ? t('teams.locked') : t('teams.unlocked') }}
            </span>
            <span v-if="currentTeam.isBanned" class="font-medium text-destructive">{{ t('teams.banned') }}</span>
            <code>{{ currentTeam.inviteToken }}</code>
          </div>
        </div>
        <div v-if="!canAccessChallenges" class="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">
          {{ currentTeam.isBanned ? t('teams.bannedDetail') : t('teams.waitingApproval') }}
        </div>
      </div>
      <div v-else class="grid gap-3 lg:grid-cols-2">
        <div class="grid gap-2">
          <Input v-model="newTeamName" :placeholder="t('teams.teamNamePlaceholder')" />
          <select
            v-if="competition?.tracksEnabled"
            v-model="selectedTrackName"
            class="h-10 rounded-md border bg-background px-3 text-sm"
          >
            <option value="">{{ t('teams.selectTrack') }}</option>
            <option v-for="track in competition.trackNames" :key="track" :value="track">{{ track }}</option>
          </select>
          <Button
            :disabled="!newTeamName.trim() || (competition?.tracksEnabled && !selectedTrackName) || createTeamMutation.isPending.value"
            @click="createTeamMutation.mutate()"
          >
            <Loader2 v-if="createTeamMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('teams.createTeam') }}
          </Button>
        </div>
        <div class="flex gap-2">
          <Input v-model="joinToken" :placeholder="t('teams.tokenPlaceholder')" />
          <Button variant="outline" :disabled="!joinToken.trim() || joinByTokenMutation.isPending.value" @click="joinByTokenMutation.mutate()">
            {{ t('teams.joinByToken') }}
          </Button>
        </div>
      </div>
    </div>

    <!-- Main Content Tabs -->
    <Tabs default-value="challenges" class="w-full">
      <TabsList class="grid w-full max-w-md grid-cols-2 mb-8">
        <TabsTrigger value="challenges" class="flex items-center gap-2">
          <Puzzle class="size-4" />
          {{ t('challenges.title') }}
        </TabsTrigger>
        <TabsTrigger value="scoreboard" class="flex items-center gap-2">
          <Trophy class="size-4" />
          {{ t('scoreboard.title') }}
        </TabsTrigger>
      </TabsList>

      <TabsContent value="challenges" class="mt-0">
        <div v-if="!canAccessChallenges" class="rounded-xl border border-dashed bg-muted/30 p-10 text-center text-muted-foreground">
          {{ t('teams.challengeLocked') }}
        </div>
        <div v-else class="grid grid-cols-1 gap-6 lg:grid-cols-[220px_minmax(0,1fr)]">
          <aside class="rounded-xl border bg-card p-3 shadow-sm lg:sticky lg:top-24 lg:self-start">
            <div class="mb-3 px-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">
              {{ t('challenges.directions') }}
            </div>
            <div class="space-y-1">
              <button
                v-for="item in directionOptions"
                :key="item.direction"
                type="button"
                class="flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-sm transition-colors hover:bg-muted"
                :class="activeDirection === item.direction ? 'bg-primary text-primary-foreground shadow-sm shadow-primary/20 hover:bg-primary' : 'text-foreground'"
                @click="activeDirection = item.direction"
              >
                <span>{{ item.direction === 'ALL' ? t('common.all') : item.direction }}</span>
                <span class="text-xs tabular-nums opacity-80">{{ hideSolved ? item.unsolved : item.total }}</span>
              </button>
            </div>
            <button
              type="button"
              class="mt-4 flex w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-sm transition-colors hover:bg-muted"
              :class="hideSolved ? 'border-primary/50 bg-primary/5 text-primary' : 'text-muted-foreground'"
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
                <h3 class="text-lg font-semibold">{{ t('challenges.workspaceTitle') }}</h3>
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
                @click="openChallenge(challenge)"
                class="cursor-pointer group"
              >
                <ChallengeCard
                  :challenge="challenge"
                  :solved="solvedIds.has(challenge.id)"
                  class="transition-all duration-300 group-hover:shadow-lg group-hover:-translate-y-1 group-hover:border-primary/20"
                />
              </div>
            </div>

            <div v-if="!isLoading && filteredChallenges.length === 0" class="flex flex-col items-center justify-center py-20 text-center border-2 border-dashed rounded-xl">
              <div class="size-12 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4">
                <Puzzle class="size-6" />
              </div>
              <h3 class="text-lg font-medium">{{ t('challenges.empty') }}</h3>
              <p class="text-sm text-muted-foreground mt-1">{{ t('challenges.emptyDetail') }}</p>
            </div>
          </div>
        </div>
      </TabsContent>

      <TabsContent value="scoreboard" class="mt-0">
        <Card class="noctf-panel">
          <CardHeader>
            <CardTitle>{{ t('scoreboard.fullBoard') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <ScoreboardView :competition-id="competitionId" full />
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>

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
      @create-instance="markInstanceCreated(selectedChallenge.id)"
      @request-defense="markDefenseRequested(selectedChallenge.id)"
      @patch-uploaded="queryClient.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId) })"
      @solved="onChallengeSolved"
    />
  </div>
</template>
