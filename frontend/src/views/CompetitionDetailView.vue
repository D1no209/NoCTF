<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Select } from '@/components/ui/select'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import SectionHeader from '@/components/layout/SectionHeader.vue'
import DataState from '@/components/state/DataState.vue'
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

// Modal state
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
  if (status === 'Active' || status === 'Running') return 'default'
  if (status === 'Upcoming' || status === 'Pending') return 'secondary'
  if (status === 'Ended' || status === 'Finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
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
  <AppLayout>
    <div class="mx-auto w-full max-w-7xl space-y-6 px-4 py-6 md:px-6">
      <DataState
        :loading="isLoading"
        :empty="!competition"
        :loading-title="t('common.loading')"
        :empty-title="t('competitions.empty')"
      >
        <!-- Competition Header -->
        <PageHeader
          v-if="competition"
          :title="competition.title"
          :description="competition.description"
          back-to="/competitions"
          :back-label="t('nav.backToCompetitions')"
        >
          <template #actions>
            <Badge :variant="statusVariant(competition.status)" class="mt-1">
              {{ competition.status }}
            </Badge>
          </template>
        </PageHeader>

        <div v-if="competition" class="flex flex-wrap gap-4 rounded-md border bg-card px-4 py-3 text-sm text-muted-foreground">
          <span>{{ t('competitions.startLabel') }} {{ formatDate(competition.startTime) }}</span>
          <span>{{ t('competitions.endLabel') }} {{ formatDate(competition.endTime) }}</span>
        </div>

        <div class="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_360px]">
          <!-- Challenges Grid -->
          <div class="space-y-4">
            <SectionHeader :title="t('challenges.title')">
              <template #actions>
                <div class="flex flex-wrap items-center gap-2">
                  <Select v-model="typeFilter" class="w-36">
                    <option value="all">{{ t('common.all') }} {{ t('common.type') }}</option>
                    <option v-for="type in challengeTypes" :key="type" :value="type">{{ type }}</option>
                  </Select>
                  <Select v-model="solveFilter" class="w-36">
                    <option value="all">{{ t('common.all') }}</option>
                    <option value="solved">{{ t('challenges.solved') }}</option>
                    <option value="unsolved">{{ t('challenges.unsolved') }}</option>
                  </Select>
                </div>
              </template>
              <span v-if="challenges" class="text-sm text-muted-foreground">
                {{ t('challenges.solvedCount', { solved: solvedIds.size, total: challenges.length }) }}
              </span>
            </SectionHeader>

            <DataState
              :empty="filteredChallenges.length === 0"
              :empty-title="t('challenges.empty')"
            >
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div
                v-for="challenge in filteredChallenges"
                :key="challenge.id"
                @click="openChallenge(challenge)"
              >
                <ChallengeCard
                  :challenge="challenge"
                  :solved="solvedIds.has(challenge.id)"
                />
              </div>
            </div>
            </DataState>
          </div>

          <!-- Scoreboard -->
          <aside class="lg:sticky lg:top-20 lg:self-start">
            <ScoreboardView
              :competition-id="competitionId"
            />
          </aside>
        </div>
      </DataState>
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
  </AppLayout>
</template>
