<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { 
  Select, 
  SelectContent, 
  SelectItem, 
  SelectTrigger, 
  SelectValue 
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import PageHeader from '@/components/layout/PageHeader.vue'
import ChallengeCard from '@/components/game/ChallengeCard.vue'
import ChallengeModal from '@/components/game/ChallengeModal.vue'
import ScoreboardView from '@/components/game/ScoreboardView.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Trophy, Puzzle, Calendar, CheckCircle2 } from 'lucide-vue-next'

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
}

interface Challenge {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  description?: string | null
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

const solvedIds = computed(() => new Set((submissionsResponse.value?.solvedChallenges ?? []).map((s) => s.challengeId)))

// UI State
const modalOpen = ref(false)
const selectedChallenge = ref<Challenge | null>(null)
const typeFilter = ref('all')
const solveFilter = ref('all')

function openChallenge(challenge: Challenge) {
  selectedChallenge.value = challenge
  modalOpen.value = true
}

function onChallengeSolved() {
  refetchSubmissions()
  queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) })
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
const filteredChallenges = computed(() => {
  return (challenges.value ?? []).filter((challenge) => {
    const solved = solvedIds.value.has(challenge.id)
    const matchesType = typeFilter.value === 'all' || challenge.typeId.toLowerCase() === typeFilter.value
    const matchesSolve = solveFilter.value === 'all' || (solveFilter.value === 'solved' ? solved : !solved)
    return matchesType && matchesSolve
  })
})

const challengeTypes = computed(() => {
  return Array.from(new Set((challenges.value ?? []).map((challenge) => challenge.typeId.toLowerCase()))).sort()
})
</script>

<template>
  <div class="mx-auto w-full max-w-[1600px] space-y-8 px-4 py-8 md:px-6">
    <!-- Header Section -->
    <div v-if="isLoading" class="space-y-4">
      <Skeleton class="h-10 w-1/3" />
      <Skeleton class="h-6 w-1/2" />
    </div>
    <div v-else-if="competition" class="flex flex-col md:flex-row md:items-end justify-between gap-6">
      <PageHeader
        :title="competition.title"
        :description="competition.description"
      >
        <template #actions>
          <div class="flex items-center gap-2">
            <Badge :variant="statusVariant(competition.status)" class="font-bold uppercase tracking-tighter text-[10px]">
              {{ competition.status }}
            </Badge>
            <Badge variant="outline" class="font-mono text-[10px] uppercase tracking-widest bg-muted/30">
              MODE: CTF
            </Badge>
          </div>
        </template>
      </PageHeader>

      <div class="flex items-center gap-6 text-xs text-muted-foreground bg-card/50 backdrop-blur-sm border rounded-full px-5 py-2.5 shadow-sm">
        <div class="flex items-center gap-2">
          <Calendar class="size-3.5 text-primary" />
          <span>{{ formatDate(competition.startTime) }} — {{ formatDate(competition.endTime) }}</span>
        </div>
        <div class="h-3 w-px bg-border" />
        <div class="flex items-center gap-2">
          <CheckCircle2 class="size-3.5 text-green-500" />
          <span>{{ solvedIds.size }} / {{ challenges?.length || 0 }} {{ t('challenges.solved') }}</span>
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
          {{ t('leaderboard.title', 'Leaderboard') }}
        </TabsTrigger>
      </TabsList>

      <TabsContent value="challenges" class="mt-0">
        <div class="grid grid-cols-1 gap-8 lg:grid-cols-[1fr_380px]">
          <!-- Challenges Section -->
          <div class="space-y-6">
            <div class="flex flex-wrap items-center gap-3">
              <Select v-model="typeFilter">
                <SelectTrigger class="w-[160px]">
                  <SelectValue :placeholder="t('common.type')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{{ t('common.all') }} {{ t('common.type') }}</SelectItem>
                  <SelectItem v-for="type in challengeTypes" :key="type" :value="type" class="capitalize">
                    {{ type }}
                  </SelectItem>
                </SelectContent>
              </Select>

              <Select v-model="solveFilter">
                <SelectTrigger class="w-[160px]">
                  <SelectValue :placeholder="t('common.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{{ t('common.all') }}</SelectItem>
                  <SelectItem value="solved">{{ t('challenges.solved') }}</SelectItem>
                  <SelectItem value="unsolved">{{ t('challenges.unsolved') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div v-if="isLoading" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Skeleton v-for="i in 6" :key="i" class="h-32 rounded-xl" />
            </div>
            <div v-else v-auto-animate class="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
              <p class="text-sm text-muted-foreground mt-1">{{ t('challenges.emptyDetail', 'No challenges matching your filters.') }}</p>
            </div>
          </div>

          <!-- Quick Scoreboard Sidebar -->
          <aside class="hidden lg:block space-y-6">
            <div class="flex items-center justify-between px-1">
              <h3 class="text-sm font-bold uppercase tracking-widest text-muted-foreground">
                {{ t('leaderboard.topTeams', 'Top Teams') }}
              </h3>
              <Trophy class="size-4 text-amber-500" />
            </div>
            <ScoreboardView
              :competition-id="competitionId"
              class="border-none shadow-none bg-transparent"
            />
          </aside>
        </div>
      </TabsContent>

      <TabsContent value="scoreboard" class="mt-0">
        <Card class="border-none shadow-md bg-card/50 backdrop-blur-sm">
          <CardHeader>
            <CardTitle>{{ t('leaderboard.fullBoard', 'Full Leaderboard') }}</CardTitle>
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
      @solved="onChallengeSolved"
    />
  </div>
</template>
