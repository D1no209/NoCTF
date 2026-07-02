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
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ArrowRight, Calendar, LayoutDashboard, ListChecks, ShieldCheck, Trophy, Users } from 'lucide-vue-next'

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
    .slice(0, 3)
})

const recentTeams = computed(() => (teams.value ?? []).slice(0, 3))
const approvedTeams = computed(() => (teams.value ?? []).filter(team => team.registrationStatus === 'approved').length)
const pendingTeams = computed(() => (teams.value ?? []).filter(team => team.registrationStatus === 'pending').length)

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'approved') return 'default'
  if (s === 'rejected' || s === 'banned') return 'destructive'
  if (s === 'finished') return 'outline'
  return 'secondary'
}

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
    <div class="mx-auto w-full max-w-[1600px] space-y-8 px-4 py-8 md:px-6">
      <section class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px]">
        <div class="noctf-panel overflow-hidden rounded-xl p-6 md:p-8">
          <div class="max-w-3xl space-y-5">
            <Badge variant="secondary" class="w-fit">{{ t('home.badge') }}</Badge>
            <div class="space-y-3">
              <h1 class="text-3xl font-bold tracking-tight text-foreground md:text-4xl">
                {{ t('home.title', { name: auth.user?.userName ?? 'NoCTF' }) }}
              </h1>
              <p class="max-w-2xl text-base leading-7 text-muted-foreground">
                {{ t('home.subtitle') }}
              </p>
            </div>
            <div class="flex flex-col gap-3 sm:flex-row">
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

        <Card class="noctf-panel">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-base">
              <ShieldCheck class="size-4 text-primary" />
              {{ t('home.statusTitle') }}
            </CardTitle>
          </CardHeader>
          <CardContent class="grid gap-3">
            <div class="grid grid-cols-2 gap-3">
              <div class="rounded-lg border bg-background/70 p-4">
                <div class="text-2xl font-bold tabular-nums">{{ teams?.length ?? 0 }}</div>
                <div class="text-xs text-muted-foreground">{{ t('home.joinedTeams') }}</div>
              </div>
              <div class="rounded-lg border bg-background/70 p-4">
                <div class="text-2xl font-bold tabular-nums">{{ approvedTeams }}</div>
                <div class="text-xs text-muted-foreground">{{ t('home.readyTeams') }}</div>
              </div>
            </div>
            <div class="rounded-lg border bg-background/70 p-4">
              <div class="flex items-center justify-between gap-3">
                <span class="text-sm text-muted-foreground">{{ t('home.pendingReview') }}</span>
                <Badge :variant="pendingTeams ? 'secondary' : 'outline'">{{ pendingTeams }}</Badge>
              </div>
            </div>
          </CardContent>
        </Card>
      </section>

      <section class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
        <Card>
          <CardHeader class="flex flex-row items-center justify-between gap-4">
            <div>
              <CardTitle>{{ t('home.competitionTitle') }}</CardTitle>
              <p class="mt-1 text-sm text-muted-foreground">{{ t('home.competitionSubtitle') }}</p>
            </div>
            <Button variant="outline" size="sm" as-child>
              <RouterLink to="/competitions">{{ t('common.all') }}</RouterLink>
            </Button>
          </CardHeader>
          <CardContent>
            <div v-if="loadingCompetitions" class="grid gap-3">
              <Skeleton v-for="i in 3" :key="i" class="h-24 rounded-lg" />
            </div>
            <div v-else-if="activeCompetitions.length === 0" class="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">
              {{ t('competitions.empty') }}
            </div>
            <div v-else class="grid gap-3">
              <div
                v-for="competition in activeCompetitions"
                :key="competition.id"
                class="grid gap-3 rounded-lg border bg-background/70 p-4 md:grid-cols-[1fr_auto] md:items-center"
              >
                <div class="min-w-0 space-y-2">
                  <div class="flex flex-wrap items-center gap-2">
                    <h3 class="truncate font-semibold">{{ competition.title }}</h3>
                    <Badge :variant="statusVariant(competition.status)">{{ competition.status }}</Badge>
                    <Badge variant="secondary">{{ competition.gameModeType || 'CTF' }}</Badge>
                  </div>
                  <div class="flex items-center gap-2 text-xs text-muted-foreground">
                    <Calendar class="size-3.5" />
                    <span>{{ formatDate(competition.startTime) }} - {{ formatDate(competition.endTime) }}</span>
                  </div>
                </div>
                <div class="flex flex-wrap gap-2">
                  <Button size="sm" variant="outline" as-child>
                    <RouterLink :to="`/competitions/${competition.id}/register`">{{ t('teams.registerForCompetition') }}</RouterLink>
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
          </CardContent>
        </Card>

        <Card>
          <CardHeader class="flex flex-row items-center justify-between gap-4">
            <div>
              <CardTitle>{{ t('home.teamTitle') }}</CardTitle>
              <p class="mt-1 text-sm text-muted-foreground">{{ t('home.teamSubtitle') }}</p>
            </div>
            <Button variant="outline" size="sm" as-child>
              <RouterLink to="/teams">{{ t('nav.teams') }}</RouterLink>
            </Button>
          </CardHeader>
          <CardContent>
            <div v-if="loadingTeams" class="grid gap-3">
              <Skeleton v-for="i in 3" :key="i" class="h-20 rounded-lg" />
            </div>
            <div v-else-if="recentTeams.length === 0" class="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">
              {{ t('teams.emptyMine') }}
            </div>
            <div v-else class="grid gap-3">
              <RouterLink
                v-for="team in recentTeams"
                :key="team.id"
                :to="`/competitions/${team.competitionId}/register`"
                class="rounded-lg border bg-background/70 p-4 transition-colors hover:border-primary/40 hover:bg-primary/5"
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <div class="truncate font-semibold">{{ team.name }}</div>
                    <div class="mt-1 truncate text-xs text-muted-foreground">{{ team.competitionTitle }}</div>
                  </div>
                  <Badge :variant="statusVariant(team.registrationStatus)">{{ team.registrationStatus }}</Badge>
                </div>
              </RouterLink>
            </div>
          </CardContent>
        </Card>
      </section>

      <section class="grid gap-4 md:grid-cols-3">
        <div class="rounded-xl border bg-card p-5">
          <ListChecks class="mb-3 size-5 text-primary" />
          <h3 class="font-semibold">{{ t('home.stepRegisterTitle') }}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepRegisterText') }}</p>
        </div>
        <div class="rounded-xl border bg-card p-5">
          <Users class="mb-3 size-5 text-primary" />
          <h3 class="font-semibold">{{ t('home.stepTeamTitle') }}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepTeamText') }}</p>
        </div>
        <div class="rounded-xl border bg-card p-5">
          <Trophy class="mb-3 size-5 text-primary" />
          <h3 class="font-semibold">{{ t('home.stepCompeteTitle') }}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepCompeteText') }}</p>
        </div>
      </section>
    </div>
  </AppLayout>
</template>
