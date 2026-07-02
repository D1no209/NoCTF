<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toast } from 'vue-sonner'
import { teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import EmptyState from '@/components/state/EmptyState.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { ArrowRight, Copy, KeyRound, Loader2, Lock, LogOut, ShieldAlert, Users } from 'lucide-vue-next'

const { t } = useI18n()
const queryClient = useQueryClient()
const joinToken = ref('')

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

const { data: teams, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<MyTeam[]>(),
})

const activeTeams = computed(() => (teams.value ?? []).filter(team => !team.isBanned))
const bannedTeams = computed(() => (teams.value ?? []).filter(team => team.isBanned))

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

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'approved') return 'default'
  if (s === 'rejected') return 'destructive'
  return 'secondary'
}

async function copyToken(token: string) {
  await navigator.clipboard.writeText(token)
  toast.success(t('teams.tokenCopied'))
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1500px] space-y-8 px-4 py-8 md:px-6">
      <div class="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <PageHeader :title="t('teams.pageTitle')" :description="t('teams.pageDescription')" />
        <Button as-child>
          <RouterLink to="/competitions">
            <Users class="size-4" />
            {{ t('teams.findCompetition') }}
          </RouterLink>
        </Button>
      </div>

      <Card>
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

      <div v-if="isLoading" class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <Skeleton v-for="i in 6" :key="i" class="h-52 rounded-xl" />
      </div>

      <ErrorState
        v-else-if="isError"
        :title="t('teams.loadError')"
        :retry-label="t('common.refresh')"
        @retry="refetch()"
      />

      <EmptyState
        v-else-if="!teams?.length"
        :title="t('teams.emptyMine')"
        :description="t('teams.emptyMineDescription')"
        :action-label="t('teams.findCompetition')"
        @action="$router.push('/competitions')"
      />

      <template v-else>
        <section class="space-y-4">
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-semibold">{{ t('teams.activeTeams') }}</h2>
            <Badge variant="secondary">{{ activeTeams.length }}</Badge>
          </div>

          <div v-if="activeTeams.length === 0" class="rounded-xl border border-dashed bg-card p-8 text-center text-sm text-muted-foreground">
            {{ t('teams.noActiveTeams') }}
          </div>

          <div v-else class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <Card v-for="team in activeTeams" :key="team.id" class="overflow-hidden">
              <CardHeader class="space-y-3">
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <CardTitle class="truncate text-lg">{{ team.name }}</CardTitle>
                    <p class="mt-1 truncate text-sm text-muted-foreground">{{ team.competitionTitle }}</p>
                  </div>
                  <Badge :variant="statusVariant(team.registrationStatus)">{{ team.registrationStatus }}</Badge>
                </div>
                <div class="flex flex-wrap gap-2">
                  <Badge variant="secondary">{{ team.gameModeType }}</Badge>
                  <Badge v-if="team.trackName" variant="outline">{{ team.trackName }}</Badge>
                  <Badge variant="outline">{{ team.memberCount }} / {{ team.maxTeamMembers }}</Badge>
                </div>
              </CardHeader>
              <CardContent class="space-y-4">
                <div class="flex items-center justify-between gap-3 rounded-lg bg-muted/50 px-3 py-2 text-sm">
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
            <ShieldAlert class="size-5 text-destructive" />
            <h2 class="text-lg font-semibold">{{ t('teams.bannedTeams') }}</h2>
          </div>
          <div class="grid gap-3">
            <div
              v-for="team in bannedTeams"
              :key="team.id"
              class="flex flex-col gap-2 rounded-xl border bg-card p-4 sm:flex-row sm:items-center sm:justify-between"
            >
              <div>
                <div class="font-medium">{{ team.name }}</div>
                <div class="text-sm text-muted-foreground">{{ team.competitionTitle }}</div>
              </div>
              <Badge variant="destructive">{{ t('teams.banned') }}</Badge>
            </div>
          </div>
        </section>
      </template>
    </div>
  </AppLayout>
</template>
