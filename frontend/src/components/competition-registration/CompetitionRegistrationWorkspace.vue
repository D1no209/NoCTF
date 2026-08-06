<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, ArrowRight, Calendar, CheckCircle2, Loader2, Lock, ShieldAlert, ShieldCheck, Users } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import {
  teamBanAppealApi,
  TeamBanAppealStatus,
  TeamBanSource,
} from '@/api/teamBanAppealApi'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const competitionId = computed(() => route.params.id as string)
const selectedTeamId = ref('')
const appealDraft = ref('')

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

const { data: globalTeams, isLoading: loadingGlobalTeams } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: teamApi.listMine,
})

const { data: banCase } = useQuery({
  queryKey: computed(() => queryKeys.myTeamBanCase(competitionId.value)),
  queryFn: () => teamBanAppealApi.getMy(competitionId.value),
  enabled: computed(() => !!competitionId.value && !!currentTeam.value),
})

const approvedTeam = computed(() =>
  currentTeam.value?.registrationStatus === 'approved' ? currentTeam.value : null,
)

const eligibleTeams = computed(() => (globalTeams.value ?? []).filter(team =>
  team.invitationToken && team.memberCount <= (competition.value?.maxTeamMembers ?? 0),
))

watch(eligibleTeams, (teams) => {
  if (!teams.some(team => team.id === selectedTeamId.value))
    selectedTeamId.value = teams[0]?.id ?? ''
}, { immediate: true })

const registerTeamMutation = useMutation({
  mutationFn: () => teamApi.register(competitionId.value, selectedTeamId.value),
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeam(competitionId.value) })
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    toast.success(t('teams.registrationCreated'))
  },
  onError: () => toast.error(t('teams.registrationCreateError')),
})

const submitAppealMutation = useMutation({
  mutationFn: () => teamBanAppealApi.submit(
    competitionId.value,
    appealDraft.value.trim(),
  ),
  onSuccess: () => {
    appealDraft.value = ''
    queryClient.invalidateQueries({
      queryKey: queryKeys.myTeamBanCase(competitionId.value),
    })
    toast.success(t('teams.banAppeal.submitSuccess'))
  },
  onError: () => toast.error(t('teams.banAppeal.submitError')),
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
  if (!iso)
    return '-'
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function appealStatusKey(status?: number) {
  if (status === TeamBanAppealStatus.Accepted)
    return 'accepted'
  if (status === TeamBanAppealStatus.Upheld)
    return 'upheld'
  return 'submitted'
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
                <div v-if="loadingTeams || loadingGlobalTeams" class="flex items-center gap-2 text-sm text-muted-foreground">
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
                      </div>
                    </div>
                  </Panel>

                  <div v-if="currentTeam.registrationStatus !== 'approved'" class="rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground">
                    {{ currentTeam.registrationStatus === 'rejected' ? t('teams.rejectedDetail') : t('teams.waitingApproval') }}
                  </div>

                  <Panel
                    v-if="banCase"
                    class="space-y-4 border-l-4 p-4"
                    :class="banCase.isCurrentlyBanned ? 'border-l-destructive' : 'border-l-emerald-600'"
                  >
                    <div class="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <p class="flex items-center gap-2 font-semibold">
                          <ShieldAlert v-if="banCase.isCurrentlyBanned" class="size-4 text-destructive" />
                          <ShieldCheck v-else class="size-4 text-emerald-700" />
                          {{ banCase.isCurrentlyBanned
                            ? t('teams.banAppeal.currentBanTitle')
                            : t('teams.banAppeal.correctedTitle') }}
                        </p>
                        <p class="mt-1 text-xs text-muted-foreground">
                          {{ t('teams.banAppeal.sourceLabel') }}:
                          {{ banCase.source === TeamBanSource.CheatIncident
                            ? t('teams.banAppeal.source.cheatIncident')
                            : t('teams.banAppeal.source.manualModeration') }}
                          · {{ formatDate(banCase.bannedAt ?? '') }}
                        </p>
                      </div>
                      <Badge v-if="banCase.appeal" variant="outline">
                        {{ t(`teams.banAppeal.status.${appealStatusKey(banCase.appeal.status)}`) }}
                      </Badge>
                    </div>

                    <template v-if="banCase.appeal">
                      <div class="border-l-2 border-border pl-3">
                        <p class="text-xs font-bold uppercase tracking-[0.12em] text-muted-foreground">
                          {{ t('teams.banAppeal.statement') }}
                        </p>
                        <p class="mt-1 whitespace-pre-wrap text-sm">
                          {{ banCase.appeal.statement }}
                        </p>
                      </div>
                      <div v-if="banCase.appeal.resolutionReason" class="border-l-2 border-primary/30 pl-3">
                        <p class="text-xs font-bold uppercase tracking-[0.12em] text-muted-foreground">
                          {{ t('teams.banAppeal.resolution') }}
                        </p>
                        <p class="mt-1 whitespace-pre-wrap text-sm">
                          {{ banCase.appeal.resolutionReason }}
                        </p>
                      </div>
                    </template>

                    <div v-else-if="banCase.canAppeal" class="space-y-3">
                      <p class="text-sm text-muted-foreground">
                        {{ t('teams.banAppeal.privateDescription') }}
                      </p>
                      <Textarea
                        v-model="appealDraft"
                        rows="4"
                        maxlength="512"
                        :placeholder="t('teams.banAppeal.placeholder')"
                      />
                      <div class="flex justify-end">
                        <Button
                          :disabled="appealDraft.trim().length < 16 || submitAppealMutation.isPending.value"
                          @click="submitAppealMutation.mutate()"
                        >
                          <Loader2 v-if="submitAppealMutation.isPending.value" class="size-4 animate-spin" />
                          {{ t('teams.banAppeal.submit') }}
                        </Button>
                      </div>
                    </div>
                    <p v-else-if="banCase.isCurrentlyBanned" class="text-sm text-muted-foreground">
                      {{ t('teams.banAppeal.captainOnly') }}
                    </p>
                  </Panel>
                </div>

                <div v-else>
                  <Panel class="space-y-4 p-5">
                    <div>
                      <h3 class="font-semibold">
                        {{ t('teams.registerExistingTitle') }}
                      </h3>
                      <p class="mt-1 text-sm text-muted-foreground">
                        {{ t('teams.registerExistingDescription') }}
                      </p>
                    </div>
                    <template v-if="eligibleTeams.length">
                      <Select v-model="selectedTeamId">
                        <SelectTrigger>
                          <SelectValue :placeholder="t('teams.selectTeam')" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem
                            v-for="team in eligibleTeams"
                            :key="team.id"
                            :value="team.id"
                          >
                            {{ team.name }} · {{ t('teams.globalMemberCount', { count: team.memberCount }) }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <div class="flex flex-wrap justify-end gap-2">
                        <Button variant="outline" as-child>
                          <RouterLink to="/teams">
                            {{ t('teams.openTeamManagement') }}
                          </RouterLink>
                        </Button>
                        <Button
                          :disabled="!selectedTeamId || registerTeamMutation.isPending.value"
                          @click="registerTeamMutation.mutate()"
                        >
                          <Loader2 v-if="registerTeamMutation.isPending.value" class="size-4 animate-spin" />
                          {{ t('teams.registerForCompetition') }}
                        </Button>
                      </div>
                    </template>
                    <div v-else class="flex flex-col items-start gap-3 border-2 border-dashed border-border p-4">
                      <p class="text-sm text-muted-foreground">
                        {{ t('teams.noEligibleGlobalTeams') }}
                      </p>
                      <Button as-child>
                        <RouterLink to="/teams">
                          {{ t('teams.openTeamManagement') }}
                          <ArrowRight class="size-4" />
                        </RouterLink>
                      </Button>
                    </div>
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
