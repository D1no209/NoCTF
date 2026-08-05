<script setup lang="ts">
import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '@/api/generated/types.gen'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Loader2, RefreshCw } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { cheatIncidentApi } from '@/api/cheatIncidentApi'
import {
  competitionAdminApi,
  competitionRuntimeAdminApi,
  competitionTeamAdminApi,
} from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionChallengesPanel from '@/components/admin/competition-detail/AdminCompetitionChallengesPanel.vue'
import AdminCompetitionCheatIncidentsPanel from '@/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue'
import AdminCompetitionLeaderboardVisibilityPanel from '@/components/admin/competition-detail/AdminCompetitionLeaderboardVisibilityPanel.vue'
import AdminCompetitionPermissionsPanel from '@/components/admin/competition-detail/AdminCompetitionPermissionsPanel.vue'
import AdminCompetitionRuntimesPanel from '@/components/admin/competition-detail/AdminCompetitionRuntimesPanel.vue'
import AdminCompetitionSettingsPanel from '@/components/admin/competition-detail/AdminCompetitionSettingsPanel.vue'
import AdminCompetitionTeamsPanel from '@/components/admin/competition-detail/AdminCompetitionTeamsPanel.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const route = useRoute()
const router = useRouter()
const qc = useQueryClient()
const { t } = useI18n()
const competitionId = computed(() => String(route.params.id))
const competitionModes = ['CTF', 'AWD', 'AWDP', 'KoH'] as const
const competitionStatuses = ['Draft', 'Visible', 'Published', 'Running', 'Paused', 'Finished'] as const
const sections = [
  { key: 'settings', labelKey: 'admin.competitionDetail.navSettings' },
  { key: 'permissions', labelKey: 'admin.competitionDetail.navPermissions' },
  { key: 'challenges', labelKey: 'admin.competitionDetail.navChallenges' },
  { key: 'teams', labelKey: 'admin.competitionDetail.navTeams' },
  { key: 'cheats', labelKey: 'admin.competitionDetail.navCheats' },
  { key: 'runtimes', labelKey: 'admin.competitionDetail.navRuntimes' },
] as const
type Section = typeof sections[number]['key']

const competitionForm = reactive({
  title: '',
  description: '',
  startTime: '',
  endTime: '',
  teamRegistrationAutoApprove: true,
  maxTeamMembers: 5,
  maxConcurrentRuntimeInstancesPerTeam: 0,
})

function toDateTimeLocal(value?: string) {
  if (!value)
    return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

const {
  data: competition,
  isLoading: loadingCompetition,
  refetch: refetchCompetition,
} = useQuery({
  queryKey: computed(() => queryKeys.adminCompetition(competitionId.value)),
  queryFn: () => competitionAdminApi.get(competitionId.value),
  enabled: computed(() => Boolean(competitionId.value)),
})

const {
  data: competitionTeams,
  isLoading: loadingTeams,
  refetch: refetchTeams,
} = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionTeams(competitionId.value)),
  queryFn: () => competitionTeamAdminApi.list(competitionId.value),
  enabled: computed(() => Boolean(competitionId.value)),
})

const {
  data: runtimes,
  isLoading: loadingRuntimes,
  refetch: refetchRuntimes,
} = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionRuntimes(competitionId.value)),
  queryFn: () => competitionRuntimeAdminApi.list(competitionId.value),
  enabled: computed(() => Boolean(competitionId.value)),
})

const cheatSummaryWindow = (() => {
  const to = new Date()
  return {
    from: new Date(to.getTime() - 60 * 60 * 1000).toISOString(),
    to: to.toISOString(),
    limit: 1,
  }
})()

const { data: cheatSummary } = useQuery({
  queryKey: computed(() => [
    ...queryKeys.adminCompetitionCheatIncidents(competitionId.value),
    'summary',
  ]),
  queryFn: () => cheatIncidentApi.list(competitionId.value, cheatSummaryWindow),
  enabled: computed(() => Boolean(competitionId.value)),
  refetchInterval: 15_000,
})

watch(competition, (value) => {
  if (!value)
    return
  competitionForm.title = value.title ?? ''
  competitionForm.description = value.description ?? ''
  competitionForm.startTime = toDateTimeLocal(value.startTime)
  competitionForm.endTime = toDateTimeLocal(value.endTime)
  competitionForm.teamRegistrationAutoApprove = value.teamRegistrationAutoApprove ?? true
  competitionForm.maxTeamMembers = value.maxTeamMembers ?? 5
  competitionForm.maxConcurrentRuntimeInstancesPerTeam = value.maxConcurrentRuntimeInstancesPerTeam ?? 0
}, { immediate: true })

function competitionPayload() {
  const start = new Date(competitionForm.startTime)
  const end = new Date(competitionForm.endTime)
  if (!competitionForm.title.trim() || !Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start)
    throw new TypeError('Competition metadata is invalid.')

  return {
    title: competitionForm.title.trim(),
    description: competitionForm.description.trim() || undefined,
    startTime: start.toISOString(),
    endTime: end.toISOString(),
    teamRegistrationAutoApprove: competitionForm.teamRegistrationAutoApprove,
    maxTeamMembers: Number(competitionForm.maxTeamMembers),
    maxConcurrentRuntimeInstancesPerTeam: Number(competitionForm.maxConcurrentRuntimeInstancesPerTeam),
  }
}

const saveCompetitionMutation = useMutation({
  mutationFn: () => competitionAdminApi.update(competitionId.value, competitionPayload()),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetition(competitionId.value) })
    toast.success(t('admin.competitionDetail.saveCompetitionSuccess'))
  },
  onError: () => toast.error(t('admin.competitionDetail.saveCompetitionError')),
})

function refreshTeams() {
  qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionTeams(competitionId.value) })
}

const approveTeamMutation = useMutation({
  mutationFn: (teamId: string) => competitionTeamAdminApi.approve(competitionId.value, teamId),
  onSuccess: () => {
    refreshTeams()
    toast.success(t('admin.competitionDetail.teamApproved'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const rejectTeamMutation = useMutation({
  mutationFn: (teamId: string) => competitionTeamAdminApi.reject(competitionId.value, teamId),
  onSuccess: () => {
    refreshTeams()
    toast.success(t('admin.competitionDetail.teamRejected'))
  },
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const banTeamMutation = useMutation({
  mutationFn: (teamId: string) => competitionTeamAdminApi.ban(competitionId.value, teamId, 'suspected cheat'),
  onSuccess: () => toast.success(t('admin.competitionDetail.teamBanned')),
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const unbanTeamMutation = useMutation({
  mutationFn: (teamId: string) => competitionTeamAdminApi.unban(competitionId.value, teamId),
  onSuccess: () => toast.success(t('admin.competitionDetail.teamUnbanned')),
  onError: () => toast.error(t('admin.competitionDetail.teamActionError')),
})

const runtimeOperatingId = ref<string | null>(null)
const runtimeMutation = useMutation({
  mutationFn: async ({ action, runtime }: {
    action: 'reset' | 'stop'
    runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse
  }) => {
    runtimeOperatingId.value = runtime.id ?? null
    return action === 'reset'
      ? competitionRuntimeAdminApi.reset(competitionId.value, runtime)
      : competitionRuntimeAdminApi.stop(competitionId.value, runtime)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitionRuntimes(competitionId.value) })
    toast.success(t('admin.competitionDetail.runtimeOperationAccepted'))
  },
  onError: () => toast.error(t('admin.competitionDetail.runtimeOperationError')),
  onSettled: () => { runtimeOperatingId.value = null },
})

const activeSection = computed<Section>(() => {
  const section = route.query.section
  return typeof section === 'string' && sections.some(item => item.key === section)
    ? section as Section
    : 'settings'
})

const statusLabel = computed(() => competition.value?.status === undefined
  ? null
  : competitionStatuses[competition.value.status])
const modeLabel = computed(() => competition.value?.mode === undefined
  ? null
  : competitionModes[competition.value.mode])

function sectionRoute(section: Section) {
  return { name: 'admin-competition-detail', params: { id: competitionId.value }, query: { section } }
}

async function refreshAll() {
  await Promise.all([refetchCompetition(), refetchTeams(), refetchRuntimes()])
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="router.push({ name: 'admin-competitions' })">
          <ArrowLeft class="mr-2 size-4" />{{ t('admin.competitions.title') }}
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">
            {{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}
          </h2>
          <div class="mt-2 flex flex-wrap gap-2">
            <Badge v-if="modeLabel" variant="secondary">
              {{ modeLabel }}
            </Badge>
            <Badge v-if="statusLabel" variant="outline">
              {{ statusLabel }}
            </Badge>
          </div>
        </div>
      </div>
      <Button variant="outline" size="sm" @click="refreshAll">
        <RefreshCw class="mr-2 size-4" />{{ t('common.refresh') }}
      </Button>
    </div>

    <div v-if="loadingCompetition" class="flex items-center justify-center py-12 text-sm text-muted-foreground">
      <Loader2 class="mr-2 size-4 animate-spin" />{{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <Tabs v-else :model-value="activeSection" @update:model-value="(value: string | undefined) => value && router.replace(sectionRoute(value as Section))">
      <TabsList class="h-auto flex-wrap justify-start">
        <TabsTrigger v-for="section in sections" :key="section.key" :value="section.key">
          {{ t(section.labelKey) }}
          <Badge v-if="section.key === 'cheats' && cheatSummary?.pendingCount" variant="destructive" class="ml-2">
            {{ cheatSummary.pendingCount }}
          </Badge>
        </TabsTrigger>
      </TabsList>

      <div class="grid min-w-0 gap-6">
        <TabsContent value="settings">
          <div v-if="activeSection === 'settings'" class="space-y-6">
            <AdminCompetitionSettingsPanel :competition-form="competitionForm" :saving="saveCompetitionMutation.isPending.value" @save="saveCompetitionMutation.mutate()" />
            <AdminCompetitionLeaderboardVisibilityPanel :competition-id="competitionId" />
          </div>
        </TabsContent>
        <TabsContent value="permissions">
          <AdminCompetitionPermissionsPanel v-if="activeSection === 'permissions'" :competition-id="competitionId" />
        </TabsContent>
        <TabsContent value="challenges">
          <AdminCompetitionChallengesPanel v-if="activeSection === 'challenges'" :competition-id="competitionId" />
        </TabsContent>
        <TabsContent value="teams">
          <AdminCompetitionTeamsPanel
            v-if="activeSection === 'teams'"
            :competition-teams="competitionTeams"
            :loading-teams="loadingTeams"
            :max-team-members="competitionForm.maxTeamMembers"
            @approve="approveTeamMutation.mutate($event)"
            @reject="rejectTeamMutation.mutate($event)"
            @ban="banTeamMutation.mutate($event)"
            @unban="unbanTeamMutation.mutate($event)"
          />
        </TabsContent>
        <TabsContent value="cheats" class="min-w-0">
          <AdminCompetitionCheatIncidentsPanel
            v-if="activeSection === 'cheats'"
            :competition-id="competitionId"
            :competition-teams="competitionTeams"
          />
        </TabsContent>
        <TabsContent value="runtimes">
          <AdminCompetitionRuntimesPanel
            v-if="activeSection === 'runtimes'"
            :runtimes="runtimes"
            :loading="loadingRuntimes"
            :operating-id="runtimeOperatingId"
            @reset="runtimeMutation.mutate({ action: 'reset', runtime: $event })"
            @stop="runtimeMutation.mutate({ action: 'stop', runtime: $event })"
          />
        </TabsContent>
      </div>
    </Tabs>
  </div>
</template>
