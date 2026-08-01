<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, ArrowRight, Calendar, CheckCircle2, Loader2, Lock, LogOut, Users } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const competitionId = computed(() => route.params.id as string)

const { data: competition, isLoading: loadingCompetition, isError: competitionError, refetch: refetchCompetition } = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: currentTeam, isLoading: loadingTeams } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeam(competitionId.value)),
  queryFn: () => teamApi.getMy(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const approvedTeam = computed(() =>
  currentTeam.value?.registrationStatus === 'approved' ? currentTeam.value : null,
)

const leaveTeamMutation = useMutation({
  mutationFn: () => teamApi.leave(competitionId.value),
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeam(competitionId.value) })
    toast.success(t('teams.leaveSuccess'))
  },
  onError: () => toast.error(t('teams.leaveError')),
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'approved')
    return 'default'
  if (s === 'rejected')
    return 'destructive'
  if (s === 'finished')
    return 'outline'
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
    <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <Button variant="ghost" size="sm" as-child class="-ml-2">
        <RouterLink to="/competitions">
          <ArrowLeft class="size-4" />
          {{ t('nav.backToCompetitions') }}
        </RouterLink>
      </Button>

      <div v-if="loadingCompetition" class="space-y-4">
        <Skeleton class="h-10 w-1/3" />
        <Skeleton class="h-24 rounded-xl" />
      </div>

      <ErrorState
        v-else-if="competitionError"
        :title="t('competitions.loadError')"
        :retry-label="t('common.refresh')"
        @retry="refetchCompetition()"
      />

      <template v-else-if="competition">
        <Card class="px-4 py-4">
          <div class="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
            <PageHeader :title="competition.title" :description="competition.description ?? t('teams.registrationDetailFallback')">
              <template #actions>
                <div class="flex flex-wrap gap-2">
                  <Badge :variant="statusVariant(competition.status)">
                    {{ competition.status }}
                  </Badge>
                  <Badge variant="secondary">
                    {{ competition.mode.toUpperCase() }}
                  </Badge>
                  <Badge variant="outline">
                    {{ t('teams.maxMembers', { count: competition.maxTeamMembers }) }}
                  </Badge>
                </div>
              </template>
            </PageHeader>
            <div class="grid gap-2 text-sm text-muted-foreground sm:grid-cols-2 lg:min-w-[420px]">
              <Panel class="flex-row items-center gap-2 px-4 py-2">
                <Calendar class="size-4 text-primary" />
                <span>{{ formatDate(competition.startTime) }}</span>
              </Panel>
              <Panel class="flex-row items-center gap-2 px-4 py-2">
                <CheckCircle2 class="size-4 text-primary" />
                <span>{{ competition.teamRegistrationAutoApprove ? t('teams.autoApprove') : t('teams.requiresReview') }}</span>
              </Panel>
            </div>
          </div>
        </Card>

        <section class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
          <Card class="px-4 py-4">
            <div class="space-y-3">
              <div>
                <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-[0.12em]">
                  <Users class="size-5 text-primary" />
                  {{ t('teams.registrationTitle') }}
                </div>
                <p class="mt-2 text-sm text-muted-foreground">
                  {{ t('teams.registrationDescription') }}
                </p>
              </div>
              <Separator />
              <div>
                <div v-if="loadingTeams" class="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 class="size-4 animate-spin" />
                  {{ t('common.loading') }}
                </div>

                <div v-else-if="currentTeam" class="space-y-5">
                  <Panel class="p-5">
                    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                      <div class="space-y-2">
                        <div class="flex flex-wrap items-center gap-2">
                          <h3 class="text-lg font-semibold">
                            {{ currentTeam.name }}
                          </h3>
                          <Badge :variant="statusVariant(currentTeam.registrationStatus)">
                            {{ currentTeam.registrationStatus }}
                          </Badge>
                        </div>
                        <div class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
                          <span>{{ currentTeam.memberCount }} / {{ competition.maxTeamMembers }} {{ t('common.members') }}</span>
                          <span class="inline-flex items-center gap-1">
                            <Lock class="size-3.5" />
                            {{ currentTeam.isLocked ? t('teams.locked') : t('teams.unlocked') }}
                          </span>
                        </div>
                      </div>
                      <div class="flex flex-wrap gap-2">
                        <Button v-if="approvedTeam" as-child>
                          <RouterLink :to="`/competitions/${competition.id}`">
                            {{ t('competitions.enter') }}
                            <ArrowRight class="size-4" />
                          </RouterLink>
                        </Button>
                        <Button variant="outline" as-child>
                          <RouterLink to="/teams">
                            {{ t('teams.manageTeam') }}
                            <ArrowRight class="size-4" />
                          </RouterLink>
                        </Button>
                        <Button
                          v-if="!currentTeam.isLocked"
                          variant="outline"
                          :disabled="leaveTeamMutation.isPending.value"
                          @click="leaveTeamMutation.mutate()"
                        >
                          <Loader2 v-if="leaveTeamMutation.isPending.value" class="size-4 animate-spin" />
                          <LogOut v-else class="size-4" />
                          {{ t('teams.leave') }}
                        </Button>
                      </div>
                    </div>
                  </Panel>

                  <div v-if="currentTeam.registrationStatus !== 'approved'" class="rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground">
                    {{ currentTeam.registrationStatus === 'rejected' ? t('teams.rejectedDetail') : t('teams.waitingApproval') }}
                  </div>
                </div>

                <div v-else>
                  <Panel class="flex flex-col items-start gap-4 p-5">
                    <div>
                      <h3 class="font-semibold">
                        {{ t('teams.managedOnTeamsTitle') }}
                      </h3>
                      <p class="mt-1 text-sm text-muted-foreground">
                        {{ t('teams.managedOnTeamsDescription') }}
                      </p>
                    </div>
                    <Button as-child>
                      <RouterLink to="/teams">
                        {{ t('teams.openTeamManagement') }}
                        <ArrowRight class="size-4" />
                      </RouterLink>
                    </Button>
                  </Panel>
                </div>
              </div>
            </div>
          </Card>

          <Card class="px-4 py-4">
            <div class="space-y-3">
              <div class="text-sm font-bold uppercase tracking-[0.12em]">
                {{ t('teams.registrationRules') }}
              </div>
              <Separator />
              <div class="space-y-3 text-sm text-muted-foreground">
                <Panel class="p-4">
                  {{ t('teams.ruleOneTeam') }}
                </Panel>
                <Panel class="p-4">
                  {{ competition.teamRegistrationAutoApprove ? t('teams.ruleAutoApprove') : t('teams.ruleManualReview') }}
                </Panel>
              </div>
            </div>
          </Card>
        </section>
      </template>
    </div>
  </AppLayout>
</template>
