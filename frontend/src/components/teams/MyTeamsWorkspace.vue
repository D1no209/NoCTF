<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { ArrowRight, Inbox, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { loadMyTeamRegistrations } from '@/components/teams/myTeamRegistrations'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()

const {
  data: registrations,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => loadMyTeamRegistrations(),
})

const registeredCount = computed(() =>
  registrations.value?.filter(item => item.team !== null).length ?? 0,
)

function teamStatusVariant(status: 'pending' | 'approved' | 'rejected') {
  if (status === 'approved')
    return 'default'
  if (status === 'rejected')
    return 'destructive'
  return 'secondary'
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1400px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <PageHeader
        :title="t('teams.workspaceTitle')"
        :description="t('teams.workspaceDescription')"
      >
        <template #actions>
          <Badge variant="outline">
            {{ t('teams.registrationCount', { count: registeredCount }) }}
          </Badge>
        </template>
      </PageHeader>

      <div v-if="isLoading" class="grid gap-4 lg:grid-cols-2">
        <Skeleton v-for="index in 4" :key="index" class="h-48 border-2 border-border" />
      </div>

      <ErrorState
        v-else-if="isError"
        :title="t('errors.loadMyTeams')"
        :retry-label="t('common.refresh')"
        @retry="refetch()"
      />

      <Card
        v-else-if="!registrations?.length"
        class="flex min-h-64 flex-col items-center justify-center border-dashed px-6 py-10 text-center"
      >
        <Inbox class="size-9 text-muted-foreground" />
        <h2 class="mt-4 font-semibold">
          {{ t('teams.noCompetitions') }}
        </h2>
        <p class="mt-1 max-w-md text-sm text-muted-foreground">
          {{ t('teams.noCompetitionsDescription') }}
        </p>
        <Button class="mt-5" as-child>
          <RouterLink to="/competitions">
            {{ t('nav.competitions') }}
          </RouterLink>
        </Button>
      </Card>

      <div v-else class="grid gap-4 lg:grid-cols-2">
        <Card
          v-for="item in registrations"
          :key="item.competition.id"
          class="flex h-full flex-col gap-5 p-5"
        >
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="min-w-0">
              <div class="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                <UsersRound class="size-4" />
                {{ item.competition.mode.toUpperCase() }}
              </div>
              <h2 class="mt-2 truncate text-lg font-bold">
                {{ item.competition.title }}
              </h2>
            </div>
            <Badge variant="secondary">
              {{ t(`competitions.status.${item.competition.status}`) }}
            </Badge>
          </div>

          <Panel v-if="item.team" class="flex-1 space-y-3 p-4">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <div>
                <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  {{ t('teams.currentRegistration') }}
                </p>
                <p class="mt-1 text-lg font-semibold">
                  {{ item.team.name }}
                </p>
              </div>
              <Badge :variant="teamStatusVariant(item.team.registrationStatus)">
                {{ t(`teams.status.${item.team.registrationStatus}`) }}
              </Badge>
            </div>
            <p class="text-sm text-muted-foreground">
              {{ t('teams.memberSummary', {
                current: item.team.memberCount,
                maximum: item.competition.maxTeamMembers,
              }) }}
            </p>
          </Panel>

          <Panel v-else class="flex flex-1 items-center p-4">
            <div>
              <p class="font-semibold">
                {{ t('teams.notRegistered') }}
              </p>
              <p class="mt-1 text-sm text-muted-foreground">
                {{ t('teams.notRegisteredDetail') }}
              </p>
            </div>
          </Panel>

          <Button as-child :variant="item.team ? 'outline' : 'default'">
            <RouterLink :to="`/competitions/${item.competition.id}/register`">
              {{ item.team ? t('teams.manageRegistration') : t('teams.registerForCompetition') }}
              <ArrowRight class="size-4" />
            </RouterLink>
          </Button>
        </Card>
      </div>
    </div>
  </AppLayout>
</template>
