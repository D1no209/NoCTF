<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { ArrowRight, CalendarDays, Inbox, LockKeyhole, UsersRound } from 'lucide-vue-next'
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

const { locale, t } = useI18n()

const {
  data: registrations,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => loadMyTeamRegistrations(),
})

function teamStatusVariant(status: 'pending' | 'approved' | 'rejected') {
  if (status === 'approved')
    return 'default'
  if (status === 'rejected')
    return 'destructive'
  return 'secondary'
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
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
            {{ t('teams.teamCount', { count: registrations?.length ?? 0 }) }}
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
          {{ t('teams.noTeams') }}
        </h2>
        <p class="mt-1 max-w-md text-sm text-muted-foreground">
          {{ t('teams.noTeamsDescription') }}
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
          :key="item.team.id"
          class="flex h-full flex-col gap-5 p-5"
        >
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="min-w-0">
              <div class="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                <UsersRound class="size-4" />
                {{ t('teams.teamLabel') }}
              </div>
              <h2 class="mt-2 truncate text-lg font-bold">
                {{ item.team.name }}
              </h2>
            </div>
            <Badge :variant="teamStatusVariant(item.team.registrationStatus)">
              {{ t(`teams.status.${item.team.registrationStatus}`) }}
            </Badge>
          </div>

          <Panel class="grid flex-1 gap-4 p-4 sm:grid-cols-2">
            <div>
              <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                {{ t('teams.competition') }}
              </p>
              <div class="mt-1 flex flex-wrap items-center gap-2">
                <p class="font-semibold">
                  {{ item.competition.title }}
                </p>
                <Badge variant="outline">
                  {{ item.competition.mode.toUpperCase() }}
                </Badge>
              </div>
            </div>
            <div>
              <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                {{ t('teams.members') }}
              </p>
              <p class="mt-1 font-semibold">
                {{ t('teams.memberSummary', {
                  current: item.team.memberCount,
                  maximum: item.competition.maxTeamMembers,
                }) }}
              </p>
            </div>
            <div>
              <p class="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                <CalendarDays class="size-3.5" />
                {{ t('teams.registeredAt') }}
              </p>
              <p class="mt-1 font-semibold">
                {{ formatDate(item.team.registeredAt) }}
              </p>
            </div>
            <div>
              <p class="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                <LockKeyhole class="size-3.5" />
                {{ t('teams.rosterState') }}
              </p>
              <p class="mt-1 font-semibold">
                {{ item.team.isLocked ? t('teams.locked') : t('teams.unlocked') }}
              </p>
            </div>
          </Panel>

          <Button as-child variant="outline">
            <RouterLink :to="`/competitions/${item.competition.id}/register`">
              {{ t('teams.manageTeam') }}
              <ArrowRight class="size-4" />
            </RouterLink>
          </Button>
        </Card>
      </div>
    </div>
  </AppLayout>
</template>
