<script setup lang="ts">
import { ArrowLeft, Loader2, RefreshCw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import AdminCompetitionChallengesPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionChallengesPanel.vue'
import AdminCompetitionCheatIncidentsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue'
import AdminCompetitionLogsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionLogsPanel.vue'
import AdminCompetitionSettingsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionSettingsPanel.vue'
import AdminCompetitionTeamsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionTeamsPanel.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/ui-v1/components/ui/tabs'
import { useAdminCompetitionDetailPage } from '@/features/admin/useAdminCompetitionDetailPage'

const { t } = useI18n()

const competitionDetailSections = [
  { key: 'settings', labelKey: 'admin.competitionDetail.navSettings' },
  { key: 'challenges', labelKey: 'admin.competitionDetail.navChallenges' },
  { key: 'teams', labelKey: 'admin.competitionDetail.navTeams' },
  { key: 'cheats', labelKey: 'admin.competitionDetail.navCheats' },
  { key: 'logs', labelKey: 'admin.competitionDetail.navLogs' },
] as const

const {
  competitionId,
  competitionForm,
  competition,
  loadingCompetition,
  competitionChallenges,
  loadingChallenges,
  competitionTeams,
  loadingTeams,
  competitionLogs,
  loadingLogs,
  cheatIncidents,
  loadingCheatIncidents,
  saveCompetitionMutation: saveCompetition,
  deleteChallengeMutation: deleteChallenge,
  approveTeamMutation: approveTeam,
  rejectTeamMutation: rejectTeam,
  lockTeamMutation: lockTeam,
  banTeamMutation: banTeam,
  unbanTeamMutation: unbanTeam,
  restartContainerMutation: restartContainer,
  rebuildScoreboardMutation: rebuildScoreboard,
  activeSection,
  switchSection,
  canOpenAwdpScreen,
  goAdminCompetitions,
} = useAdminCompetitionDetailPage()

const saveCompetitionMutation = useToastMutation(saveCompetition, {
  success: 'admin.competitionDetail.saveCompetitionSuccess',
  error: 'admin.competitionDetail.saveCompetitionError',
})

const deleteChallengeMutation = useToastMutation<string>(deleteChallenge, {
  success: 'admin.competitionDetail.removeChallengeSuccess',
  error: 'admin.competitionDetail.removeChallengeError',
})

const approveTeamMutation = useToastMutation<string>(approveTeam, {
  success: 'admin.competitionDetail.teamApproved',
  error: 'admin.competitionDetail.teamActionError',
})

const rejectTeamMutation = useToastMutation<string>(rejectTeam, {
  success: 'admin.competitionDetail.teamRejected',
  error: 'admin.competitionDetail.teamActionError',
})

const lockTeamMutation = useToastMutation<{ teamId: string, isLocked: boolean }>(lockTeam, {
  success: 'admin.competitionDetail.teamLockUpdated',
  error: 'admin.competitionDetail.teamActionError',
})

const banTeamMutation = useToastMutation<string>(banTeam, {
  success: 'admin.competitionDetail.teamBanned',
  error: 'admin.competitionDetail.teamActionError',
})

const unbanTeamMutation = useToastMutation<string>(unbanTeam, {
  success: 'admin.competitionDetail.teamUnbanned',
  error: 'admin.competitionDetail.teamActionError',
})

const restartContainerMutation = useToastMutation<string>(restartContainer, {
  success: 'admin.competitionDetail.containerRestarted',
  error: 'admin.competitionDetail.containerRestartError',
})

const rebuildScoreboardMutation = useToastMutation(rebuildScoreboard, {
  success: (data) => {
    const rows = (data as { rows?: number } | undefined)?.rows
    return rows === undefined
      ? t('admin.competitionDetail.scoreboardRebuilt')
      : t('admin.competitionDetail.scoreboardRebuiltWithTeams', { count: rows })
  },
  error: 'admin.competitionDetail.scoreboardRebuildError',
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="goAdminCompetitions">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitions.title') }}
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">
            {{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}
          </h2>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.competitionDetail.subtitle') }}
          </p>
        </div>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="rebuildScoreboardMutation.isPending.value"
          @click="rebuildScoreboardMutation.mutate()"
        >
          <Loader2 v-if="rebuildScoreboardMutation.isPending.value" class="size-4 animate-spin" />
          <RefreshCw v-else class="size-4" />
          {{ t('admin.competitionDetail.rebuildScoreboard') }}
        </Button>
        <Badge v-if="competition?.status" variant="outline" class="capitalize">
          {{ t(`competitions.status.${competition.status.toLowerCase()}`, competition.status) }}
        </Badge>
      </div>
    </div>

    <div v-if="loadingCompetition" class="flex flex-col items-center justify-center py-12 text-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <Tabs
      v-else
      :model-value="activeSection"
      @update:model-value="(value: string | undefined) => value && switchSection(value)"
    >
      <TabsList class="h-auto flex-wrap justify-start">
        <TabsTrigger
          v-for="section in competitionDetailSections.slice(0, 3)"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
        <Button variant="ghost" size="sm" class="h-8 rounded-none px-3" as-child>
          <RouterLink :to="{ name: 'admin-competition-operations', params: { id: competitionId } }">
            {{ t('admin.competitionDetail.navOperations') }}
          </RouterLink>
        </Button>
        <Button v-if="canOpenAwdpScreen" variant="ghost" size="sm" class="h-8 rounded-none px-3" as-child>
          <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
            {{ t('awdp.screenEntry') }}
          </RouterLink>
        </Button>
        <TabsTrigger
          v-for="section in competitionDetailSections.slice(3)"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
      </TabsList>

      <div class="grid gap-6">
        <section class="space-y-6">
          <TabsContent value="settings">
            <AdminCompetitionSettingsPanel
              v-if="activeSection === 'settings'"
              :competition-form="competitionForm"
              :saving="saveCompetitionMutation.isPending.value"
              @save="saveCompetitionMutation.mutate()"
            />
          </TabsContent>

          <TabsContent value="challenges">
            <AdminCompetitionChallengesPanel
              v-if="activeSection === 'challenges'"
              :competition-id="competitionId"
              :challenges="competitionChallenges"
              :loading="loadingChallenges"
              :deleting="deleteChallengeMutation.isPending.value"
              :restarting="restartContainerMutation.isPending.value"
              @delete="deleteChallengeMutation.mutate($event)"
              @restart="restartContainerMutation.mutate($event)"
            />
          </TabsContent>

          <TabsContent value="teams">
            <AdminCompetitionTeamsPanel
              v-if="activeSection === 'teams'"
              :competition-teams="competitionTeams"
              :loading-teams="loadingTeams"
              :max-team-members="competitionForm.maxTeamMembers"
              @approve="approveTeamMutation.mutate($event)"
              @reject="rejectTeamMutation.mutate($event)"
              @toggle-lock="lockTeamMutation.mutate($event)"
              @toggle-ban="$event.isBanned ? unbanTeamMutation.mutate($event.teamId) : banTeamMutation.mutate($event.teamId)"
            />
          </TabsContent>

          <TabsContent value="cheats">
            <AdminCompetitionCheatIncidentsPanel
              v-if="activeSection === 'cheats'"
              :cheat-incidents="cheatIncidents"
              :loading-cheat-incidents="loadingCheatIncidents"
              @ban="banTeamMutation.mutate($event)"
            />
          </TabsContent>

          <TabsContent value="logs">
            <AdminCompetitionLogsPanel
              v-if="activeSection === 'logs'"
              :competition-logs="competitionLogs"
              :loading-logs="loadingLogs"
            />
          </TabsContent>
        </section>
      </div>
    </Tabs>
  </div>
</template>
