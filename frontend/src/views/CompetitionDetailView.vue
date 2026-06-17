<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, RouterLink } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { client } from '@/api/generated/client.gen'
import { Badge } from '@/components/ui/badge'
import ChallengeCard from '@/components/game/ChallengeCard.vue'
import ChallengeModal from '@/components/game/ChallengeModal.vue'
import ScoreboardView from '@/components/game/ScoreboardView.vue'

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
  queryKey: computed(() => ['competition', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: Competition }, unknown, false>({
      url: '/api/competitions/{id}',
      path: { id: competitionId.value },
    })
    return res.data ?? null
  },
  enabled: computed(() => !!competitionId.value),
})

const { data: challenges, isLoading: loadingChallenges } = useQuery({
  queryKey: computed(() => ['challenges', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: Challenge[] }, unknown, false>({
      url: '/api/competitions/{id}/challenges',
      path: { id: competitionId.value },
    })
    return res.data ?? []
  },
  enabled: computed(() => !!competitionId.value),
})

const { data: submissionsResponse, refetch: refetchSubmissions } = useQuery({
  queryKey: computed(() => ['submissions', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: SubmissionsResponse }, unknown, false>({
      url: '/api/competitions/{id}/submissions',
      path: { id: competitionId.value },
    })
    return res.data ?? null
  },
  enabled: computed(() => !!competitionId.value),
})

const solvedIds = computed(() => new Set((submissionsResponse.value?.solvedChallenges ?? []).map((s) => s.challengeId)))

// Modal state
const modalOpen = ref(false)
const selectedChallenge = ref<Challenge | null>(null)

function openChallenge(challenge: Challenge) {
  selectedChallenge.value = challenge
  modalOpen.value = true
}

function onChallengeSolved() {
  refetchSubmissions()
  queryClient.invalidateQueries({ queryKey: ['submissions', competitionId.value] })
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 'Active' || status === 'Running') return 'default'
  if (status === 'Upcoming' || status === 'Pending') return 'secondary'
  if (status === 'Ended' || status === 'Finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

const isLoading = computed(() => loadingComp.value || loadingChallenges.value)
</script>

<template>
  <div class="min-h-screen bg-background p-8">
    <div class="max-w-6xl mx-auto">
      <div class="mb-6">
        <RouterLink to="/competitions" class="text-sm text-muted-foreground hover:text-foreground transition-colors">
          ← {{ t('nav.backToCompetitions') }}
        </RouterLink>
      </div>

      <div v-if="isLoading" class="text-muted-foreground">{{ t('common.loading') }}</div>

      <template v-else>
        <!-- Competition Header -->
        <div class="mb-8" v-if="competition">
          <div class="flex items-start gap-3 mb-2">
            <h1 class="text-3xl font-bold tracking-tight">{{ competition.title }}</h1>
            <Badge :variant="statusVariant(competition.status)" class="mt-1">
              {{ competition.status }}
            </Badge>
          </div>
          <p v-if="competition.description" class="text-muted-foreground mb-3">
            {{ competition.description }}
          </p>
          <div class="flex gap-6 text-sm text-muted-foreground">
            <span>{{ t('competitions.startLabel') }} {{ formatDate(competition.startTime) }}</span>
            <span>{{ t('competitions.endLabel') }} {{ formatDate(competition.endTime) }}</span>
          </div>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <!-- Challenges Grid -->
          <div class="lg:col-span-2">
            <div class="flex items-center justify-between mb-4">
              <h2 class="text-xl font-semibold">{{ t('challenges.title') }}</h2>
              <span v-if="challenges" class="text-sm text-muted-foreground">
                {{ t('challenges.solvedCount', { solved: solvedIds.size, total: challenges.length }) }}
              </span>
            </div>

            <div v-if="!challenges || challenges.length === 0" class="text-muted-foreground">
              {{ t('challenges.empty') }}
            </div>

            <div v-else class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div
                v-for="challenge in challenges"
                :key="challenge.id"
                @click="openChallenge(challenge)"
              >
                <ChallengeCard
                  :challenge="challenge"
                  :solved="solvedIds.has(challenge.id)"
                />
              </div>
            </div>
          </div>

          <!-- Scoreboard -->
          <div class="lg:col-span-1">
            <ScoreboardView
              :competition-id="competitionId"
            />
          </div>
        </div>
      </template>
    </div>

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
