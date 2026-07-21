<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AppLayout from '@/ui-v1/components/layout/AppLayout.vue'
import HomeCompetitionList from '@/ui-v1/components/home/HomeCompetitionList.vue'
import HomeStatusCard from '@/ui-v1/components/home/HomeStatusCard.vue'
import HomeTeamList from '@/ui-v1/components/home/HomeTeamList.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent } from '@/ui-v1/components/ui/card'
import { LayoutDashboard, ListChecks, Trophy, Users } from 'lucide-vue-next'
import { useHomePage } from '@/features/home/useHomePage'
import { useChromeSession } from '@/features/chrome/useChromeSession'

const { t } = useI18n()
const { userName } = useChromeSession()

const {
  canManage,
  teams,
  activeCompetitions: watchedCompetitions,
  recentTeams: myRecentTeams,
  approvedTeams,
  pendingTeams,
  loadingCompetitions,
  loadingTeams,
} = useHomePage()

const activeCompetitions = computed(() => watchedCompetitions.value.map(competition => ({
  id: competition.id ?? '',
  title: competition.title ?? '',
  status: competition.status ?? '',
  gameModeType: competition.gameModeType,
  startTime: competition.startTime ?? '',
  endTime: competition.endTime ?? '',
})))

const recentTeams = computed(() => myRecentTeams.value.map(team => ({
  id: team.id ?? '',
  name: team.name ?? '',
  competitionId: team.competitionId ?? '',
  competitionTitle: team.competitionTitle ?? '',
  registrationStatus: team.registrationStatus ?? '',
})))
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
                {{ t('home.title', { name: userName || 'NoCTF' }) }}
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
