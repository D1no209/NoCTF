<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import AppLayout from '@/components/layout/AppLayout.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { competitionStatusVariant, gameModeVariant, registrationStatusVariant } from '@/lib/statusTones'
import { ArrowRight, Calendar, LayoutDashboard, Trophy, Users } from 'lucide-vue-next'

const { t } = useI18n()
const auth = useAuthStore()

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
}

interface MyTeam {
  id: string
  competitionId: string
  name: string
  competitionTitle: string
  registrationStatus: string
  memberCount: number
  maxTeamMembers: number
  isLocked: boolean
  isBanned: boolean
  gameModeType: string
}

const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<Competition[]>(),
})

const { data: teams, isLoading: loadingTeams } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<MyTeam[]>(),
})

const activeCompetitions = computed(() => {
  const priority = new Map([
    ['running', 0],
    ['published', 1],
    ['draft', 2],
    ['paused', 3],
    ['finished', 4],
  ])

  return [...(competitions.value ?? [])]
    .sort((a, b) => {
      const statusA = priority.get(a.status.toLowerCase()) ?? 5
      const statusB = priority.get(b.status.toLowerCase()) ?? 5
      if (statusA !== statusB) return statusA - statusB
      return new Date(a.startTime).getTime() - new Date(b.startTime).getTime()
    })
    .slice(0, 4)
})

const recentTeams = computed(() => (teams.value ?? []).slice(0, 3))
const approvedTeams = computed(
  () => (teams.value ?? []).filter((team) => team.registrationStatus === 'approved').length,
)
const pendingTeams = computed(
  () => (teams.value ?? []).filter((team) => team.registrationStatus === 'pending').length,
)

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <AppLayout>
    <div class="noctf-page">
      <section class="noctf-workbench">
        <div class="grid lg:grid-cols-[minmax(0,1fr)_360px]">
          <div class="border-b border-border/80 p-5 md:p-6 lg:border-b-0 lg:border-r">
            <div class="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
              <div class="max-w-3xl space-y-3">
                <p class="noctf-label">{{ t('home.badge') }}</p>
                <h1 class="text-2xl font-bold tracking-tight text-foreground md:text-3xl">
                  {{ t('home.title', { name: auth.user?.userName ?? 'NoCTF' }) }}
                </h1>
                <p class="max-w-2xl text-sm leading-6 text-muted-foreground">
                  {{ t('home.subtitle') }}
                </p>
              </div>
              <div class="flex flex-wrap gap-2">
                <Button as-child>
                  <RouterLink to="/competitions">
                    <Trophy class="size-4" />
                    {{ t('home.browseCompetitions') }}
                  </RouterLink>
                </Button>
                <Button variant="outline" as-child>
                  <RouterLink to="/teams">
                    <Users class="size-4" />
                    {{ t('home.manageTeams') }}
                  </RouterLink>
                </Button>
                <Button v-if="canManage" variant="secondary" as-child>
                  <RouterLink to="/admin">
                    <LayoutDashboard class="size-4" />
                    {{ t('nav.admin') }}
                  </RouterLink>
                </Button>
              </div>
            </div>
          </div>

          <div class="p-0">
            <div class="border-b border-border/80 px-4 py-3">
              <h2 class="text-sm font-semibold">{{ t('home.statusTitle') }}</h2>
            </div>
            <div class="grid grid-cols-3 divide-x divide-border/80">
              <div class="p-4">
                <div class="text-2xl font-bold tabular-nums">{{ teams?.length ?? 0 }}</div>
                <div class="mt-1 text-xs text-muted-foreground">{{ t('home.joinedTeams') }}</div>
              </div>
              <div class="p-4">
                <div class="text-2xl font-bold tabular-nums text-success">{{ approvedTeams }}</div>
                <div class="mt-1 text-xs text-muted-foreground">{{ t('home.readyTeams') }}</div>
              </div>
              <div class="p-4">
                <div class="text-2xl font-bold tabular-nums text-warning">{{ pendingTeams }}</div>
                <div class="mt-1 text-xs text-muted-foreground">{{ t('home.pendingReview') }}</div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
        <div class="noctf-workbench">
          <div
            class="flex flex-col gap-3 border-b border-border/80 p-4 sm:flex-row sm:items-start sm:justify-between"
          >
            <div>
              <h2 class="font-semibold">{{ t('home.competitionTitle') }}</h2>
              <p class="mt-1 text-sm text-muted-foreground">{{ t('home.competitionSubtitle') }}</p>
            </div>
            <Button variant="outline" size="sm" as-child>
              <RouterLink to="/competitions">{{ t('common.all') }}</RouterLink>
            </Button>
          </div>

          <div v-if="loadingCompetitions" class="grid gap-0 divide-y divide-border/80">
            <div v-for="i in 4" :key="i" class="p-4">
              <Skeleton class="h-16 rounded-md" />
            </div>
          </div>
          <div
            v-else-if="activeCompetitions.length === 0"
            class="noctf-state-box m-4 min-h-0 p-8 text-sm text-muted-foreground"
          >
            {{ t('competitions.empty') }}
          </div>
          <div v-else class="divide-y divide-border/80">
            <div
              v-for="competition in activeCompetitions"
              :key="competition.id"
              class="grid gap-3 p-4 transition-colors hover:bg-muted/35 md:grid-cols-[minmax(0,1fr)_auto] md:items-center"
            >
              <div class="min-w-0 space-y-2">
                <div class="flex flex-wrap items-center gap-2">
                  <h3 class="truncate font-semibold">{{ competition.title }}</h3>
                  <Badge :variant="competitionStatusVariant(competition.status)">{{
                    competition.status
                  }}</Badge>
                  <Badge :variant="gameModeVariant(competition.gameModeType)">{{ competition.gameModeType || 'CTF' }}</Badge>
                </div>
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <Calendar class="size-3.5" />
                  <span
                    >{{ formatDate(competition.startTime) }} -
                    {{ formatDate(competition.endTime) }}</span
                  >
                </div>
              </div>
              <div class="flex flex-wrap gap-2">
                <Button size="sm" variant="outline" as-child>
                  <RouterLink :to="`/competitions/${competition.id}/register`">
                    {{ t('teams.registerForCompetition') }}
                  </RouterLink>
                </Button>
                <Button size="sm" as-child>
                  <RouterLink :to="`/competitions/${competition.id}`">
                    {{ t('competitions.enter') }}
                    <ArrowRight class="size-4" />
                  </RouterLink>
                </Button>
              </div>
            </div>
          </div>
        </div>

        <div class="noctf-workbench">
          <div
            class="flex flex-col gap-3 border-b border-border/80 p-4 sm:flex-row sm:items-start sm:justify-between"
          >
            <div>
              <h2 class="font-semibold">{{ t('home.teamTitle') }}</h2>
              <p class="mt-1 text-sm text-muted-foreground">{{ t('home.teamSubtitle') }}</p>
            </div>
            <Button variant="outline" size="sm" as-child>
              <RouterLink to="/teams">{{ t('nav.teams') }}</RouterLink>
            </Button>
          </div>
          <div v-if="loadingTeams" class="divide-y divide-border/80">
            <div v-for="i in 3" :key="i" class="p-4">
              <Skeleton class="h-14 rounded-md" />
            </div>
          </div>
          <div
            v-else-if="recentTeams.length === 0"
            class="noctf-state-box m-4 min-h-0 p-8 text-sm text-muted-foreground"
          >
            {{ t('teams.emptyMine') }}
          </div>
          <div v-else class="divide-y divide-border/80">
            <RouterLink
              v-for="team in recentTeams"
              :key="team.id"
              :to="`/competitions/${team.competitionId}/register`"
              class="grid gap-2 p-4 transition-colors hover:bg-muted/35"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="truncate font-semibold">{{ team.name }}</div>
                  <div class="mt-1 truncate text-xs text-muted-foreground">
                    {{ team.competitionTitle }}
                  </div>
                </div>
                <Badge :variant="registrationStatusVariant(team.registrationStatus)">{{
                  team.registrationStatus
                }}</Badge>
              </div>
              <div class="text-xs text-muted-foreground">
                {{ team.memberCount }} / {{ team.maxTeamMembers }} {{ t('common.members') }}
              </div>
            </RouterLink>
          </div>
        </div>
      </section>
    </div>
  </AppLayout>
</template>
