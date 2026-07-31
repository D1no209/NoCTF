<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { LayoutDashboard, ListChecks, Trophy, Users } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import HomeCompetitionList from '@/components/home/HomeCompetitionList.vue'
import AppLayout from '@/components/layout/AppLayout.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const auth = useAuthStore()

const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list(),
})

const activeCompetitions = computed(() => {
  const priority = new Map([
    ['running', 0],
    ['published', 1],
    ['visible', 2],
    ['draft', 3],
    ['paused', 4],
    ['finished', 5],
  ])

  return [...(competitions.value ?? [])]
    .sort((a, b) => {
      const statusA = priority.get(a.status) ?? 6
      const statusB = priority.get(b.status) ?? 6
      if (statusA !== statusB)
        return statusA - statusB
      return new Date(a.startTime).getTime() - new Date(b.startTime).getTime()
    })
    .slice(0, 3)
})
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-4 px-4 py-6 md:px-6 lg:px-8">
      <section>
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
              <Button v-if="canManage" variant="secondary" as-child>
                <RouterLink to="/admin">
                  <LayoutDashboard class="size-4" />
                  {{ t('nav.admin') }}
                </RouterLink>
              </Button>
            </div>
          </div>
        </Card>
      </section>

      <section>
        <HomeCompetitionList
          :competitions="activeCompetitions"
          :loading="loadingCompetitions"
        />
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
