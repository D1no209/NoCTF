<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, watch } from 'vue'
import { useRoute, RouterView, RouterLink } from 'vue-router'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { ChevronLeft, Users } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const scoreStore = useScoreStore()

const competitionId = computed(() => route.params.id as string)
const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
const displayScore = computed(() => scoreStore.myTeamScore)
const titleText = computed(() => `NoCTF / ${t('common.live')}`)

interface MyCompetitionTeam {
  id: string
  name: string
  registrationStatus: string
  isBanned: boolean
}

interface LeaderboardEntry {
  teamId?: string | null
  teamName?: string | null
  totalScore?: number | null
}

interface LeaderboardResponse {
  entries?: LeaderboardEntry[]
}

const { data: myTeams } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<MyCompetitionTeam[]>(competitionId.value),
  enabled: computed(() => Boolean(competitionId.value)),
})

const approvedTeam = computed(() =>
  (myTeams.value ?? []).find(team => team.registrationStatus === 'approved' && !team.isBanned) ?? null,
)

const { data: leaderboard } = useQuery({
  queryKey: computed(() => queryKeys.leaderboard(competitionId.value)),
  queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<LeaderboardResponse>,
  enabled: computed(() => Boolean(competitionId.value) && Boolean(approvedTeam.value?.id)),
  refetchInterval: computed(() => approvedTeam.value?.id ? 10_000 : false),
})

watch(
  () => [competitionId.value, approvedTeam.value?.id, leaderboard.value?.entries] as const,
  () => {
    const team = approvedTeam.value
    if (!competitionId.value || !team) {
      scoreStore.setCurrentTeamScore(competitionId.value || null, null, null, null)
      return
    }

    const entry = (leaderboard.value?.entries ?? []).find(item => item.teamId === team.id)
    scoreStore.setCurrentTeamScore(
      competitionId.value,
      team.id,
      entry?.teamName ?? team.name,
      entry?.totalScore ?? 0,
    )
  },
  { immediate: true },
)
</script>

<template>
  <div class="min-h-[100dvh] flex flex-col bg-transparent">
    <header class="sticky top-0 z-50 w-full border-b-2 border-border bg-muted">
      <div class="mx-auto flex h-16 max-w-[1600px] items-center justify-between gap-4 px-4 md:px-6">
        <div class="flex min-w-0 items-center gap-4">
          <Button variant="outline" size="sm" as-child>
            <RouterLink to="/competitions" class="flex items-center gap-2">
              <ChevronLeft class="size-4" />
              <span class="hidden sm:inline">{{ t('nav.back') }}</span>
            </RouterLink>
          </Button>

          <div class="flex min-w-0 items-center gap-3 text-lg font-bold tracking-[0.08em] md:text-2xl">
            <span class="truncate">{{ titleText }}</span>
          </div>
        </div>

        <div class="flex min-w-0 items-center gap-3">
          <div
            v-if="displayName"
            class="flex min-w-0 items-center gap-2 border-2 border-border bg-card px-2 py-1.5 text-sm"
          >
            <Users class="size-4 text-foreground" />
            <span class="hidden truncate font-medium md:inline">{{ displayName }}</span>
            <Badge v-if="displayScore !== null" variant="default" class="font-mono tabular-nums">
              {{ displayScore }}
            </Badge>
          </div>
          <div class="shrink-0">
            <LanguageSwitch />
          </div>
        </div>
      </div>
    </header>

    <!-- Main Game Area -->
    <main class="relative flex-1">
      <RouterView v-slot="{ Component }">
        <transition
          name="fade"
          mode="out-in"
        >
          <component :is="Component" :key="route.fullPath" />
        </transition>
      </RouterView>
    </main>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
