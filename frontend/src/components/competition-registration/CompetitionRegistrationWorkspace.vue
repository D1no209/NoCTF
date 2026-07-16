<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toast } from 'vue-sonner'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Panel } from '@/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import ErrorState from '@/components/state/ErrorState.vue'
import { ArrowLeft, ArrowRight, Calendar, CheckCircle2, Copy, KeyRound, Loader2, Lock, UserPlus, Users } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const competitionId = computed(() => route.params.id as string)
const newTeamName = ref('')
const selectedTrackName = ref('')
const joinToken = ref('')

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  startTime: string
  endTime: string
  gameModeType: string
  maxTeamMembers: number
  teamRegistrationAutoApprove: boolean
  tracksEnabled: boolean
  trackNames: string[]
}

interface MyCompetitionTeam {
  id: string
  competitionId: string
  name: string
  inviteToken: string
  memberCount: number
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  isCaptain: boolean
}

const { data: competition, isLoading: loadingCompetition, isError: competitionError, refetch: refetchCompetition } = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get<Competition>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: myTeams, isLoading: loadingTeams } = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<MyCompetitionTeam[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const currentTeam = computed(() => myTeams.value?.[0] ?? null)
const approvedTeam = computed(() => (myTeams.value ?? []).find(team => team.registrationStatus === 'approved') ?? null)

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<MyCompetitionTeam>({
    competitionId: competitionId.value,
    name: newTeamName.value.trim(),
    trackName: competition.value?.tracksEnabled ? selectedTrackName.value : undefined,
  }),
  onSuccess: () => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<MyCompetitionTeam>(joinToken.value.trim()),
  onSuccess: (team) => {
    joinToken.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'approved') return 'default'
  if (s === 'rejected') return 'destructive'
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

async function copyToken(token: string) {
  await navigator.clipboard.writeText(token)
  toast.success(t('teams.tokenCopied'))
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
                  <Badge :variant="statusVariant(competition.status)">{{ competition.status }}</Badge>
                  <Badge variant="secondary">{{ competition.gameModeType }}</Badge>
                  <Badge variant="outline">{{ t('teams.maxMembers', { count: competition.maxTeamMembers }) }}</Badge>
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
                <p class="mt-2 text-sm text-muted-foreground">{{ t('teams.registrationDescription') }}</p>
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
                          <h3 class="text-lg font-semibold">{{ currentTeam.name }}</h3>
                          <Badge :variant="statusVariant(currentTeam.registrationStatus)">
                            {{ currentTeam.registrationStatus }}
                          </Badge>
                          <Badge v-if="currentTeam.trackName" variant="outline">{{ currentTeam.trackName }}</Badge>
                        </div>
                        <div class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
                          <span>{{ currentTeam.memberCount }} / {{ competition.maxTeamMembers }} {{ t('common.members') }}</span>
                          <span class="inline-flex items-center gap-1">
                            <Lock class="size-3.5" />
                            {{ currentTeam.isLocked ? t('teams.locked') : t('teams.unlocked') }}
                          </span>
                          <span>{{ currentTeam.isCaptain ? t('teams.captain') : t('teams.member') }}</span>
                        </div>
                      </div>
                      <Button v-if="approvedTeam && !currentTeam.isBanned" as-child>
                        <RouterLink :to="`/competitions/${competition.id}`">
                          {{ t('competitions.enter') }}
                          <ArrowRight class="size-4" />
                        </RouterLink>
                      </Button>
                    </div>
                    <Panel class="mt-4 flex-row items-center justify-between gap-3 px-3 py-2">
                      <code class="truncate text-xs">{{ currentTeam.inviteToken }}</code>
                      <Button variant="ghost" size="icon-sm" @click="copyToken(currentTeam.inviteToken)">
                        <Copy class="size-4" />
                      </Button>
                    </Panel>
                  </Panel>

                  <div v-if="currentTeam.isBanned" class="rounded-lg border border-destructive/30 bg-destructive/5 p-4 text-sm text-destructive">
                    {{ t('teams.bannedDetail') }}
                  </div>
                  <div v-else-if="currentTeam.registrationStatus !== 'approved'" class="rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground">
                    {{ currentTeam.registrationStatus === 'rejected' ? t('teams.rejectedDetail') : t('teams.waitingApproval') }}
                  </div>
                </div>

                <div v-else class="grid gap-4 xl:grid-cols-2">
                  <Panel class="space-y-3 p-4">
                    <div class="flex items-center gap-2 font-semibold">
                      <UserPlus class="size-4 text-primary" />
                      {{ t('teams.createForCompetition') }}
                    </div>
                    <Input v-model="newTeamName" :placeholder="t('teams.teamNamePlaceholder')" />
                    <Select
                      v-if="competition.tracksEnabled"
                      v-model="selectedTrackName"
                    >
                      <SelectTrigger>
                        <SelectValue :placeholder="t('teams.selectTrack')" />
                      </SelectTrigger>
                      <SelectContent :body-lock="false" :disable-outside-pointer-events="false">
                        <SelectItem value="">{{ t('teams.selectTrack') }}</SelectItem>
                        <SelectItem v-for="track in competition.trackNames" :key="track" :value="track">{{ track }}</SelectItem>
                      </SelectContent>
                    </Select>
                    <Button
                      class="w-full"
                      :disabled="!newTeamName.trim() || (competition.tracksEnabled && !selectedTrackName) || createTeamMutation.isPending.value"
                      @click="createTeamMutation.mutate()"
                    >
                      <Loader2 v-if="createTeamMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                      {{ t('teams.createTeam') }}
                    </Button>
                  </Panel>

                  <Panel class="space-y-3 p-4">
                    <div class="flex items-center gap-2 font-semibold">
                      <KeyRound class="size-4 text-primary" />
                      {{ t('teams.joinExistingTeam') }}
                    </div>
                    <Input v-model="joinToken" :placeholder="t('teams.tokenPlaceholder')" />
                    <Button
                      variant="outline"
                      class="w-full"
                      :disabled="!joinToken.trim() || joinByTokenMutation.isPending.value"
                      @click="joinByTokenMutation.mutate()"
                    >
                      <Loader2 v-if="joinByTokenMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                      {{ t('teams.joinByToken') }}
                    </Button>
                  </Panel>
                </div>
              </div>
            </div>
          </Card>

          <Card class="px-4 py-4">
            <div class="space-y-3">
              <div class="text-sm font-bold uppercase tracking-[0.12em]">{{ t('teams.registrationRules') }}</div>
              <Separator />
              <div class="space-y-3 text-sm text-muted-foreground">
                <Panel class="p-4">
                  {{ t('teams.ruleOneTeam') }}
                </Panel>
                <Panel class="p-4">
                  {{ competition.teamRegistrationAutoApprove ? t('teams.ruleAutoApprove') : t('teams.ruleManualReview') }}
                </Panel>
                <Panel class="p-4">
                  {{ competition.tracksEnabled ? t('teams.ruleTrackRequired') : t('teams.ruleNoTrack') }}
                </Panel>
              </div>
            </div>
          </Card>
        </section>
      </template>
    </div>
  </AppLayout>
</template>
