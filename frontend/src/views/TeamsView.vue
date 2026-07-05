<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { AlertCircle, ArrowRight, Copy, Inbox, KeyRound, Loader2, Lock, LogOut, Plus, ShieldAlert, Users } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { gameModeVariant, registrationStatusVariant } from '@/lib/statusTones'

const { t } = useI18n()
const queryClient = useQueryClient()
const joinToken = ref('')
const newTeamName = ref('')
const selectedCompetitionId = ref('')
const selectedTrackName = ref('')

interface MyTeam {
  id: string
  competitionId: string
  name: string
  inviteToken: string
  registrationStatus: string
  competitionTitle: string
  competitionStatus: string
  gameModeType: string
  memberCount: number
  maxTeamMembers: number
  isCaptain: boolean
  isLocked: boolean
  isBanned: boolean
  trackName?: string | null
}

interface CompetitionListItem {
  id: string
  title: string
  status: string
  gameModeType: string
}

interface CompetitionDetail {
  id: string
  title: string
  tracksEnabled: boolean
  trackNames: string[]
}

const { data: teams, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<MyTeam[]>(),
})

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<CompetitionListItem[]>(),
})

const { data: selectedCompetitionDetail, isLoading: loadingSelectedCompetition } = useQuery({
  queryKey: computed(() => queryKeys.competition(selectedCompetitionId.value)),
  queryFn: () => competitionApi.get<CompetitionDetail>(selectedCompetitionId.value),
  enabled: computed(() => !!selectedCompetitionId.value),
})

const activeTeams = computed(() => (teams.value ?? []).filter(team => !team.isBanned))
const bannedTeams = computed(() => (teams.value ?? []).filter(team => team.isBanned))
const availableCompetitions = computed(() => competitions.value ?? [])
const selectedCompetitionRequiresTrack = computed(() => Boolean(selectedCompetitionDetail.value?.tracksEnabled))
const canCreateTeam = computed(() => {
  if (!selectedCompetitionId.value || !newTeamName.value.trim())
    return false
  if (selectedCompetitionRequiresTrack.value && !selectedTrackName.value)
    return false
  return true
})

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<MyTeam>({
    competitionId: selectedCompetitionId.value,
    name: newTeamName.value.trim(),
    trackName: selectedCompetitionRequiresTrack.value ? selectedTrackName.value : undefined,
  }),
  onSuccess: (team) => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

watch(selectedCompetitionId, () => {
  selectedTrackName.value = ''
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<MyTeam>(joinToken.value.trim()),
  onSuccess: (team) => {
    joinToken.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const leaveTeamMutation = useMutation({
  mutationFn: (teamId: string) => teamApi.leave(teamId),
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    toast.success(t('teams.leaveSuccess'))
  },
  onError: () => toast.error(t('teams.leaveError')),
})

async function copyToken(token: string) {
  await navigator.clipboard.writeText(token)
  toast.success(t('teams.tokenCopied'))
}
</script>

<template>
  <AppLayout>
    <div class="noctf-page">
      <div class="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <PageHeader :title="t('teams.pageTitle')" :description="t('teams.pageDescription')" />
        <Button as-child>
          <RouterLink to="/competitions">
            <Users class="size-4" />
            {{ t('teams.findCompetition') }}
          </RouterLink>
        </Button>
      </div>

      <div class="grid gap-4 lg:grid-cols-2">
        <Card class="noctf-surface">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-base">
              <Plus class="size-4 text-primary" />
              {{ t('teams.createTeam') }}
            </CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <Select v-model="selectedCompetitionId" :disabled="loadingCompetitions || availableCompetitions.length === 0">
              <SelectTrigger>
                <SelectValue :placeholder="loadingCompetitions ? t('common.loading') : t('teams.selectCompetition')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="competition in availableCompetitions"
                  :key="competition.id"
                  :value="competition.id"
                >
                  {{ competition.title }} / {{ competition.gameModeType.toUpperCase() }}
                </SelectItem>
              </SelectContent>
            </Select>

            <Input v-model="newTeamName" :placeholder="t('teams.teamNamePlaceholder')" />

            <Select
              v-if="selectedCompetitionRequiresTrack"
              v-model="selectedTrackName"
              :disabled="loadingSelectedCompetition"
            >
              <SelectTrigger>
                <SelectValue :placeholder="loadingSelectedCompetition ? t('common.loading') : t('teams.selectTrack')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="track in selectedCompetitionDetail?.trackNames ?? []"
                  :key="track"
                  :value="track"
                >
                  {{ track }}
                </SelectItem>
              </SelectContent>
            </Select>

            <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <p class="text-xs text-muted-foreground">
                {{ availableCompetitions.length === 0 ? t('teams.noCompetitionsToCreate') : t('teams.createTeamDescription') }}
              </p>
              <Button
                class="shrink-0"
                :disabled="!canCreateTeam || createTeamMutation.isPending.value"
                @click="createTeamMutation.mutate()"
              >
                <Loader2 v-if="createTeamMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                {{ t('teams.createTeam') }}
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card class="noctf-surface">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-base">
              <KeyRound class="size-4 text-primary" />
              {{ t('teams.joinExistingTeam') }}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
              <Input v-model="joinToken" :placeholder="t('teams.tokenPlaceholder')" />
              <Button
                variant="outline"
                :disabled="!joinToken.trim() || joinByTokenMutation.isPending.value"
                @click="joinByTokenMutation.mutate()"
              >
                <Loader2 v-if="joinByTokenMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                {{ t('teams.joinByToken') }}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>

      <div v-if="isLoading" class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <Skeleton v-for="i in 6" :key="i" class="h-52 rounded-xl" />
      </div>

      <div v-else-if="isError" class="noctf-state-box">
        <AlertCircle class="size-8 text-danger" />
        <h3 class="mt-3 text-sm font-medium">
          {{ t('teams.loadError') }}
        </h3>
        <Button variant="outline" size="sm" class="mt-4" @click="refetch()">
          {{ t('common.refresh') }}
        </Button>
      </div>

      <div v-else-if="!teams?.length" class="noctf-state-box">
        <Inbox class="size-8 text-muted-foreground" />
        <h3 class="mt-3 text-sm font-medium">
          {{ t('teams.emptyMine') }}
        </h3>
        <p class="mt-1 max-w-sm text-sm text-muted-foreground">
          {{ t('teams.emptyMineDescription') }}
        </p>
      </div>

      <template v-else>
        <section class="space-y-4">
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-semibold">
              {{ t('teams.activeTeams') }}
            </h2>
            <Badge variant="neutral">
              {{ activeTeams.length }}
            </Badge>
          </div>

          <div v-if="activeTeams.length === 0" class="noctf-state-box text-sm text-muted-foreground">
            {{ t('teams.noActiveTeams') }}
          </div>

          <div v-else class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <Card v-for="team in activeTeams" :key="team.id" class="overflow-hidden transition-colors hover:border-primary/30">
              <CardHeader class="space-y-3">
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <CardTitle class="truncate text-lg">
                      {{ team.name }}
                    </CardTitle>
                    <p class="mt-1 truncate text-sm text-muted-foreground">
                      {{ team.competitionTitle }}
                    </p>
                  </div>
                  <Badge :variant="registrationStatusVariant(team.registrationStatus)">
                    {{ team.registrationStatus }}
                  </Badge>
                </div>
                <div class="flex flex-wrap gap-2">
                  <Badge :variant="gameModeVariant(team.gameModeType)">
                    {{ team.gameModeType }}
                  </Badge>
                  <Badge v-if="team.trackName" variant="outline">
                    {{ team.trackName }}
                  </Badge>
                  <Badge variant="outline">
                    {{ team.memberCount }} / {{ team.maxTeamMembers }}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent class="space-y-4">
                <div class="flex items-center justify-between gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
                  <code class="truncate text-xs">{{ team.inviteToken }}</code>
                  <Button variant="ghost" size="icon-sm" @click="copyToken(team.inviteToken)">
                    <Copy class="size-4" />
                  </Button>
                </div>
                <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  <span class="inline-flex items-center gap-1">
                    <Lock class="size-3.5" />
                    {{ team.isLocked ? t('teams.locked') : t('teams.unlocked') }}
                  </span>
                  <span>{{ team.isCaptain ? t('teams.captain') : t('teams.member') }}</span>
                </div>
                <div class="flex gap-2">
                  <Button size="sm" as-child class="flex-1">
                    <RouterLink :to="`/competitions/${team.competitionId}`">
                      {{ t('competitions.enter') }}
                      <ArrowRight class="size-4" />
                    </RouterLink>
                  </Button>
                  <Button
                    v-if="!team.isLocked"
                    size="sm"
                    variant="outline"
                    :disabled="leaveTeamMutation.isPending.value"
                    @click="leaveTeamMutation.mutate(team.id)"
                  >
                    <LogOut class="size-4" />
                    {{ t('teams.leave') }}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </div>
        </section>

        <section v-if="bannedTeams.length" class="space-y-4">
          <div class="flex items-center gap-2">
            <ShieldAlert class="size-5 text-danger" />
            <h2 class="text-lg font-semibold">
              {{ t('teams.bannedTeams') }}
            </h2>
          </div>
          <div class="grid gap-3">
            <div
              v-for="team in bannedTeams"
              :key="team.id"
              class="flex flex-col gap-2 rounded-xl border bg-card p-4 sm:flex-row sm:items-center sm:justify-between"
            >
              <div>
                <div class="font-medium">
                  {{ team.name }}
                </div>
                <div class="text-sm text-muted-foreground">
                  {{ team.competitionTitle }}
                </div>
              </div>
              <Badge variant="destructive">
                {{ t('teams.banned') }}
              </Badge>
            </div>
          </div>
        </section>
      </template>
    </div>
  </AppLayout>
</template>
