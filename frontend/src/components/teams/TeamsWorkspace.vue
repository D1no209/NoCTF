<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { AlertCircle, ArrowRight, Copy, Inbox, KeyRound, Loader2, Lock, LogOut, Plus, ShieldAlert } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Panel } from '@/components/ui/panel'
import { Separator } from '@/components/ui/separator'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'

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

defineProps<{
  teams: MyTeam[]
  activeTeams: MyTeam[]
  bannedTeams: MyTeam[]
  isLoading: boolean
  isError: boolean
  loadingCompetitions: boolean
  availableCompetitions: CompetitionListItem[]
  selectedCompetitionId: string
  selectedTrackName: string
  selectedCompetitionTracks: string[]
  newTeamName: string
  joinToken: string
  selectedCompetitionRequiresTrack: boolean
  loadingSelectedCompetition: boolean
  canCreateTeam: boolean
  createPending: boolean
  joinPending: boolean
  leavePending: boolean
}>()

const emit = defineEmits<{
  'update:selectedCompetitionId': [value: string]
  'update:selectedTrackName': [value: string]
  'update:newTeamName': [value: string]
  'update:joinToken': [value: string]
  createTeam: []
  joinByToken: []
  leaveTeam: [teamId: string]
  copyToken: [token: string]
  refetch: []
}>()

const { t } = useI18n()

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'approved') return 'default'
  if (s === 'rejected') return 'destructive'
  return 'secondary'
}
</script>

<template>
  <div>
    <div class="grid gap-3 lg:grid-cols-2">
      <Card class="px-4 py-4">
        <div class="flex items-center gap-2 text-base font-bold uppercase tracking-[0.12em]">
          <Plus class="size-4 text-primary" />
          {{ t('teams.createTeam') }}
        </div>
        <Separator class="my-3" />
        <div class="space-y-3">
          <Select :model-value="selectedCompetitionId" :disabled="loadingCompetitions || availableCompetitions.length === 0" @update:model-value="emit('update:selectedCompetitionId', String($event))">
            <SelectTrigger>
              <SelectValue :placeholder="loadingCompetitions ? t('common.loading') : t('teams.selectCompetition')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="competition in availableCompetitions"
                :key="competition.id"
                :value="competition.id"
              >
                {{ competition.title }} · {{ competition.gameModeType.toUpperCase() }}
              </SelectItem>
            </SelectContent>
          </Select>

          <Input :model-value="newTeamName" :placeholder="t('teams.teamNamePlaceholder')" @update:model-value="emit('update:newTeamName', String($event))" />

          <Select
            v-if="selectedCompetitionRequiresTrack"
            :model-value="selectedTrackName"
            :disabled="loadingSelectedCompetition"
            @update:model-value="emit('update:selectedTrackName', String($event))"
          >
            <SelectTrigger>
              <SelectValue :placeholder="loadingSelectedCompetition ? t('common.loading') : t('teams.selectTrack')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="track in selectedCompetitionTracks"
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
              :disabled="!canCreateTeam || createPending"
              @click="emit('createTeam')"
            >
              <Loader2 v-if="createPending" class="mr-2 size-4 animate-spin" />
              {{ t('teams.createTeam') }}
            </Button>
          </div>
        </div>
      </Card>

      <Card class="px-4 py-4">
        <div class="flex items-center gap-2 text-base font-bold uppercase tracking-[0.12em]">
          <KeyRound class="size-4 text-primary" />
          {{ t('teams.joinExistingTeam') }}
        </div>
        <Separator class="my-3" />
        <div>
          <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
            <Input :model-value="joinToken" :placeholder="t('teams.tokenPlaceholder')" @update:model-value="emit('update:joinToken', String($event))" />
            <Button
              variant="outline"
              :disabled="!joinToken.trim() || joinPending"
              @click="emit('joinByToken')"
            >
              <Loader2 v-if="joinPending" class="mr-2 size-4 animate-spin" />
              {{ t('teams.joinByToken') }}
            </Button>
          </div>
        </div>
      </Card>
    </div>

    <div v-if="isLoading" class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" class="h-44 border-2 border-border" />
    </div>

    <Card v-else-if="isError" class="flex min-h-40 flex-col items-center justify-center border-dashed px-4 py-8 text-center">
      <AlertCircle class="size-8 text-destructive" />
      <h3 class="mt-3 text-sm font-medium">{{ t('teams.loadError') }}</h3>
      <Button variant="outline" size="sm" class="mt-4" @click="emit('refetch')">
        {{ t('common.refresh') }}
      </Button>
    </Card>

    <Card v-else-if="!teams.length" class="flex min-h-40 flex-col items-center justify-center border-dashed px-4 py-8 text-center">
      <Inbox class="size-8 text-muted-foreground" />
      <h3 class="mt-3 text-sm font-medium">{{ t('teams.emptyMine') }}</h3>
      <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('teams.emptyMineDescription') }}</p>
    </Card>

    <template v-else>
      <section class="space-y-4">
        <div class="flex items-center justify-between">
          <h2 class="text-lg font-semibold">{{ t('teams.activeTeams') }}</h2>
          <Badge variant="secondary">{{ activeTeams.length }}</Badge>
        </div>

        <Card v-if="activeTeams.length === 0" class="flex min-h-40 items-center justify-center border-dashed text-center text-sm text-muted-foreground">
          {{ t('teams.noActiveTeams') }}
        </Card>

        <div v-else class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Card v-for="team in activeTeams" :key="team.id" class="border-2 border-border bg-card">
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
              <div class="flex items-center justify-between gap-3 border-2 border-border bg-muted px-3 py-2 text-sm">
                <code class="truncate text-xs">{{ team.inviteToken }}</code>
                <Button variant="ghost" size="icon-sm" @click="emit('copyToken', team.inviteToken)">
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
                  :disabled="leavePending"
                  @click="emit('leaveTeam', team.id)"
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
          <Panel
            v-for="team in bannedTeams"
            :key="team.id"
            class="flex flex-col gap-2 border-2 p-4 sm:flex-row sm:items-center sm:justify-between"
          >
            <div>
              <div class="font-medium">{{ team.name }}</div>
              <div class="text-sm text-muted-foreground">{{ team.competitionTitle }}</div>
            </div>
            <Badge variant="destructive">{{ t('teams.banned') }}</Badge>
          </Panel>
        </div>
      </section>
    </template>
  </div>
</template>