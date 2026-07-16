<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import AppLayout from '@/components/layout/AppLayout.vue'
import HomeCompetitionList from '@/components/home/HomeCompetitionList.vue'
import HomeStatusCard from '@/components/home/HomeStatusCard.vue'
import HomeTeamList from '@/components/home/HomeTeamList.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { LayoutDashboard, ListChecks, Trophy, Users } from 'lucide-vue-next'

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
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-4 px-4 py-6 md:px-6 lg:px-8">
      <section class="grid items-stretch gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <Card class="h-full px-5 py-5 md:px-6">
          <div class="space-y-5">
            <div class="flex items-center gap-2 border-b-2 border-border pb-3">
              <Badge variant="secondary" class="w-fit">{{ t('home.badge') }}</Badge>
            </div>
            <div class="space-y-3">
              <h1 class="text-3xl font-bold tracking-[0.08em] text-foreground md:text-4xl">
                {{ t('home.title', { name: auth.user?.userName ?? 'NoCTF' }) }}
              </h1>
              <p class="max-w-2xl text-sm leading-7 text-muted-foreground">
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
        </Card>

        <section class="home-hero-side">
          <HomeStatusCard
            :joined-teams="teams?.length ?? 0"
            :ready-teams="approvedTeams"
            :pending-review="pendingTeams"
          />
        </section>
      </section>

      <section class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
        <section class="home-content-main">
          <HomeCompetitionList
            :competitions="activeCompetitions"
            :loading="loadingCompetitions"
          />
        </section>

        <section class="home-content-side">
          <HomeTeamList
            :teams="recentTeams"
            :loading="loadingTeams"
          />
        </section>
      </section>

      <section class="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent class="pt-6">
            <ListChecks class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">{{ t('home.stepRegisterTitle') }}</h3>
            <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepRegisterText') }}</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="pt-6">
            <Users class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">{{ t('home.stepTeamTitle') }}</h3>
            <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepTeamText') }}</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="pt-6">
            <Trophy class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">{{ t('home.stepCompeteTitle') }}</h3>
            <p class="mt-1 text-sm text-muted-foreground">{{ t('home.stepCompeteText') }}</p>
          </CardContent>
        </Card>
      </section>
    </div>
  </AppLayout>
</template>
