<script setup lang="ts">
import type {
  CompetitionPermissionCandidate,
  CompetitionPermissionMember,
  CompetitionPermissionRole,
  CompetitionPermissionsSnapshot,
} from './competitionPermissions'
import type {
  CompetitionPermissionCandidates,
  CompetitionPermissions,
  CompetitionPermissionsConflict,
} from '@/api/noctf'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  ArrowRight,
  Loader2,
  RefreshCw,
  ShieldCheck,
  Trash2,
  UserRoundCog,
} from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import {
  ApiError,
  competitionPermissionsAdminApi,
  readCompetitionPermissionsConflict,
} from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  assignCompetitionPermission,
  buildCompetitionPermissionUpdateRequest,
  competitionPermissionMembers,
  eligibleCompetitionPermissionCandidates,
  hasCompetitionPermissionChanges,
  normalizeCompetitionPermissionCandidates,
  normalizeCompetitionPermissionSnapshot,
  removeCompetitionPermission,
} from './competitionPermissions'

const props = defineProps<{
  competitionId: string
}>()

const roles = ['manager', 'judge', 'observer'] as const satisfies readonly CompetitionPermissionRole[]

type PermissionAction
  = | { type: 'assign', userId: string, role: CompetitionPermissionRole }
    | { type: 'remove', userId: string }

type PermissionLoadState
  = | 'loading'
    | 'restricted'
    | 'notFound'
    | 'error'
    | 'invalid'
    | 'ready'

const { t } = useI18n()
const queryClient = useQueryClient()
const revokedStatus = ref<403 | 404 | null>(null)
const selectedCandidateIds = reactive<Record<CompetitionPermissionRole, string>>({
  manager: '',
  judge: '',
  observer: '',
})

const normalizedCompetitionId = computed(() => props.competitionId.trim().toLowerCase())
const permissionsQueryKey = computed(() =>
  queryKeys.adminCompetitionPermissions(props.competitionId),
)
const candidatesQueryKey = computed(() =>
  queryKeys.adminCompetitionPermissionCandidates(props.competitionId),
)

const permissionsQuery = useQuery({
  queryKey: permissionsQueryKey,
  queryFn: () => competitionPermissionsAdminApi.get(props.competitionId),
  retry: false,
})

const candidatesQuery = useQuery({
  queryKey: candidatesQueryKey,
  queryFn: () => competitionPermissionsAdminApi.candidates(props.competitionId),
  retry: false,
})

const updateMutation = useMutation({
  mutationFn: async (action: PermissionAction) => {
    const request = buildActionUpdate(action)
    await competitionPermissionsAdminApi.update(props.competitionId, request)
  },
  retry: false,
  onSuccess: async () => {
    await refetchPermissionData()
    clearSelections()
    toast.success(t('admin.competitionDetail.permissionsUpdateSuccess'))
  },
  onError: async (error) => {
    const status = apiErrorStatus(error)
    const conflict = readCompetitionPermissionsConflict(error)

    if (status === 403 || status === 404)
      revokedStatus.value = status

    await refetchPermissionData()
    if (status !== 409)
      clearSelections()

    if (status === 409 && conflict) {
      toast.error(t(conflictMessageKey(conflict.code)))
      return
    }

    toast.error(t('admin.competitionDetail.permissionsUpdateError'))
  },
})

const snapshot = computed(() => {
  const value = normalizeCompetitionPermissionSnapshot(permissionsQuery.data.value)
  if (!value || value.competitionId !== normalizedCompetitionId.value)
    return null

  return value
})

const candidates = computed(() =>
  normalizeCompetitionPermissionCandidates(candidatesQuery.data.value),
)

const loadState = computed<PermissionLoadState>(() => {
  const statuses = [
    revokedStatus.value,
    apiErrorStatus(permissionsQuery.error.value),
    apiErrorStatus(candidatesQuery.error.value),
  ]

  if (statuses.includes(403))
    return 'restricted'
  if (statuses.includes(404))
    return 'notFound'
  if (permissionsQuery.isFetching.value || candidatesQuery.isFetching.value)
    return 'loading'
  if (permissionsQuery.isError.value || candidatesQuery.isError.value)
    return 'error'
  if (!snapshot.value || !candidates.value)
    return 'invalid'

  return 'ready'
})

const isRefreshing = computed(() =>
  permissionsQuery.isFetching.value || candidatesQuery.isFetching.value,
)

const canWrite = computed(() =>
  loadState.value === 'ready'
  && !isRefreshing.value
  && !updateMutation.isPending.value,
)

function apiErrorStatus(error: unknown) {
  return error instanceof ApiError ? error.status : undefined
}

function roleLabelKey(role: CompetitionPermissionRole) {
  switch (role) {
    case 'manager':
      return 'admin.competitionDetail.permissionsRoleManager'
    case 'judge':
      return 'admin.competitionDetail.permissionsRoleJudge'
    case 'observer':
      return 'admin.competitionDetail.permissionsRoleObserver'
  }
}

function roleDescriptionKey(role: CompetitionPermissionRole) {
  switch (role) {
    case 'manager':
      return 'admin.competitionDetail.permissionsRoleManagerDescription'
    case 'judge':
      return 'admin.competitionDetail.permissionsRoleJudgeDescription'
    case 'observer':
      return 'admin.competitionDetail.permissionsRoleObserverDescription'
  }
}

function membersFor(role: CompetitionPermissionRole) {
  if (!snapshot.value || !candidates.value)
    return []

  return competitionPermissionMembers(snapshot.value, candidates.value, role)
}

function assignedIds(value: CompetitionPermissionsSnapshot) {
  return new Set([...value.managerIds, ...value.judgeIds, ...value.observerIds])
}

function availableCandidates(role: CompetitionPermissionRole) {
  if (!snapshot.value || !candidates.value)
    return []

  const assigned = assignedIds(snapshot.value)
  return eligibleCompetitionPermissionCandidates(candidates.value, snapshot.value, role)
    .filter(candidate => !assigned.has(candidate.id))
}

function candidateOptionLabel(candidate: CompetitionPermissionCandidate) {
  return `${candidate.userName} · ${candidate.id}`
}

function canMoveMember(
  member: CompetitionPermissionMember,
  targetRole: CompetitionPermissionRole,
) {
  if (!canWrite.value || !snapshot.value || !member.candidate)
    return false

  return eligibleCompetitionPermissionCandidates(
    [member.candidate],
    snapshot.value,
    targetRole,
  ).length === 1
}

function currentRole(value: CompetitionPermissionsSnapshot, userId: string) {
  return roles.find(role => idsForRole(value, role).includes(userId)) ?? null
}

function idsForRole(value: CompetitionPermissionsSnapshot, role: CompetitionPermissionRole) {
  switch (role) {
    case 'manager':
      return value.managerIds
    case 'judge':
      return value.judgeIds
    case 'observer':
      return value.observerIds
  }
}

function readLatestSnapshot() {
  const raw = queryClient.getQueryData<CompetitionPermissions>(permissionsQueryKey.value)
  const latest = normalizeCompetitionPermissionSnapshot(raw)
  if (!latest || latest.competitionId !== normalizedCompetitionId.value)
    throw new Error('Competition permission snapshot is incomplete.')

  return latest
}

function readLatestCandidates() {
  const raw = queryClient.getQueryData<CompetitionPermissionCandidates>(candidatesQueryKey.value)
  const latest = normalizeCompetitionPermissionCandidates(raw)
  if (!latest)
    throw new Error('Competition permission candidates are incomplete.')

  return latest
}

function buildActionUpdate(action: PermissionAction) {
  const baseline = readLatestSnapshot()
  let next: CompetitionPermissionsSnapshot | null

  if (action.type === 'assign') {
    const latestCandidates = readLatestCandidates()
    const candidate = latestCandidates.find(item => item.id === action.userId)
    const isEligible = candidate
      && eligibleCompetitionPermissionCandidates(
        [candidate],
        baseline,
        action.role,
      ).length === 1

    if (!isEligible)
      throw new Error('Competition permission assignment is no longer eligible.')

    next = assignCompetitionPermission(baseline, action.userId, action.role)
  }
  else {
    if (!currentRole(baseline, action.userId))
      throw new Error('Competition permission removal is a no-op.')

    next = removeCompetitionPermission(baseline, action.userId)
  }

  if (!next || !hasCompetitionPermissionChanges(baseline, next))
    throw new Error('Competition permission update is invalid or a no-op.')

  const request = buildCompetitionPermissionUpdateRequest(next)
  if (!request)
    throw new Error('Competition permission request is incomplete.')

  return request
}

async function refetchPermissionData() {
  await Promise.all([
    permissionsQuery.refetch(),
    candidatesQuery.refetch(),
  ])
}

function clearSelections() {
  for (const role of roles)
    selectedCandidateIds[role] = ''
}

function refreshPermissionData() {
  revokedStatus.value = null
  clearSelections()
  void refetchPermissionData()
}

function assignSelected(role: CompetitionPermissionRole) {
  const userId = selectedCandidateIds[role]
  if (!userId || !availableCandidates(role).some(candidate => candidate.id === userId))
    return

  updateMutation.mutate({ type: 'assign', userId, role })
}

function moveMember(
  member: CompetitionPermissionMember,
  targetRole: CompetitionPermissionRole,
) {
  if (!canMoveMember(member, targetRole))
    return

  updateMutation.mutate({ type: 'assign', userId: member.id, role: targetRole })
}

function removeMember(member: CompetitionPermissionMember) {
  if (!canWrite.value || !snapshot.value || !currentRole(snapshot.value, member.id))
    return

  updateMutation.mutate({ type: 'remove', userId: member.id })
}

function conflictMessageKey(code: CompetitionPermissionsConflict['code']) {
  switch (code) {
    case 'RevisionConflict':
      return 'admin.competitionDetail.permissionsConflictRevision'
    case 'EmailNotVerified':
      return 'admin.competitionDetail.permissionsConflictEmailNotVerified'
    case 'RolesOverlap':
      return 'admin.competitionDetail.permissionsConflictRolesOverlap'
    case 'OwnerIncluded':
      return 'admin.competitionDetail.permissionsConflictOwnerIncluded'
    case 'UserNotFound':
      return 'admin.competitionDetail.permissionsConflictUserNotFound'
    case 'RoleNotEligible':
      return 'admin.competitionDetail.permissionsConflictRoleNotEligible'
  }
}

watch(
  [snapshot, candidates],
  () => {
    for (const role of roles) {
      const selectedId = selectedCandidateIds[role]
      if (
        selectedId
        && !availableCandidates(role).some(candidate => candidate.id === selectedId)
      ) {
        selectedCandidateIds[role] = ''
      }
    }
  },
)

watch(
  () => props.competitionId,
  () => {
    revokedStatus.value = null
    clearSelections()
    updateMutation.reset()
  },
)
</script>

<template>
  <Card class="p-4">
    <div class="mb-5 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h3 class="font-semibold">
          {{ t('admin.competitionDetail.permissionsTitle') }}
        </h3>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.competitionDetail.permissionsDescription') }}
        </p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <Badge
          v-if="loadState === 'ready' && snapshot"
          variant="outline"
          class="font-mono"
        >
          v{{ snapshot.permissionRevision }}
        </Badge>
        <Button
          variant="outline"
          size="sm"
          :disabled="isRefreshing || updateMutation.isPending.value"
          @click="refreshPermissionData"
        >
          <RefreshCw class="size-4" :class="[isRefreshing && 'animate-spin']" />
          {{ t('common.refresh') }}
        </Button>
      </div>
    </div>

    <div v-if="loadState === 'loading'" class="space-y-4">
      <p class="text-sm text-muted-foreground">
        {{ t('admin.competitionDetail.permissionsLoading') }}
      </p>
      <Skeleton class="h-20 w-full" />
      <div class="grid gap-4 xl:grid-cols-3">
        <Skeleton v-for="role in roles" :key="role" class="h-64 w-full" />
      </div>
    </div>

    <Alert v-else-if="loadState === 'restricted'" variant="warning">
      <AlertTitle>
        {{ t('admin.competitionDetail.permissionsRestrictedTitle') }}
      </AlertTitle>
      <AlertDescription>
        {{ t('admin.competitionDetail.permissionsRestrictedDescription') }}
      </AlertDescription>
    </Alert>

    <Alert v-else-if="loadState === 'notFound'" variant="warning">
      <AlertTitle>
        {{ t('admin.competitionDetail.permissionsNotFoundTitle') }}
      </AlertTitle>
      <AlertDescription>
        {{ t('admin.competitionDetail.permissionsNotFoundDescription') }}
      </AlertDescription>
    </Alert>

    <Alert v-else-if="loadState === 'error'" variant="destructive">
      <AlertTitle>
        {{ t('admin.competitionDetail.permissionsLoadErrorTitle') }}
      </AlertTitle>
      <AlertDescription>
        {{ t('admin.competitionDetail.permissionsLoadErrorDescription') }}
      </AlertDescription>
    </Alert>

    <Alert v-else-if="loadState === 'invalid'" variant="destructive">
      <AlertTitle>
        {{ t('admin.competitionDetail.permissionsInvalidTitle') }}
      </AlertTitle>
      <AlertDescription>
        {{ t('admin.competitionDetail.permissionsInvalidDescription') }}
      </AlertDescription>
    </Alert>

    <div v-else-if="snapshot && candidates" class="space-y-4">
      <div class="border bg-muted/30 p-4">
        <div class="flex flex-wrap items-center gap-2">
          <ShieldCheck class="size-4 text-muted-foreground" />
          <span class="font-medium">
            {{ t('admin.competitionDetail.permissionsOwner') }}
          </span>
          <Badge variant="secondary">
            {{ t('admin.competitionDetail.permissionsOwner') }}
          </Badge>
        </div>
        <code class="mt-2 block break-all font-mono text-xs text-muted-foreground">
          {{ snapshot.ownerId }}
        </code>
        <p class="mt-2 text-xs text-muted-foreground">
          {{ t('admin.competitionDetail.permissionsOwnerDescription') }}
        </p>
      </div>

      <div class="grid gap-4 xl:grid-cols-3">
        <Card v-for="role in roles" :key="role" class="p-0">
          <CardContent class="flex h-full min-h-80 flex-col p-4">
            <div class="border-b pb-3">
              <div class="flex items-center justify-between gap-3">
                <div class="flex items-center gap-2">
                  <UserRoundCog class="size-4 text-muted-foreground" />
                  <h4 class="font-semibold">
                    {{ t(roleLabelKey(role)) }}
                  </h4>
                </div>
                <Badge variant="outline" class="tabular-nums">
                  {{ membersFor(role).length }}
                </Badge>
              </div>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ t(roleDescriptionKey(role)) }}
              </p>
            </div>

            <div class="flex-1 space-y-2 py-3">
              <div
                v-for="member in membersFor(role)"
                :key="member.id"
                class="border p-3"
              >
                <div class="flex min-w-0 items-start justify-between gap-2">
                  <div class="min-w-0">
                    <p class="truncate text-sm font-medium">
                      {{ member.candidate?.userName ?? t('admin.competitionDetail.permissionsUnknownMember') }}
                    </p>
                    <code class="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                      {{ member.id }}
                    </code>
                  </div>
                  <Badge v-if="!member.candidate" variant="outline">
                    ID
                  </Badge>
                </div>

                <div class="mt-3 flex flex-wrap gap-1.5">
                  <Button
                    v-for="targetRole in roles.filter(item => item !== role)"
                    :key="targetRole"
                    variant="outline"
                    size="sm"
                    class="h-7 px-2 text-xs"
                    :disabled="!canMoveMember(member, targetRole)"
                    @click="moveMember(member, targetRole)"
                  >
                    <ArrowRight class="size-3" />
                    {{
                      t('admin.competitionDetail.permissionsMoveTo', {
                        role: t(roleLabelKey(targetRole)),
                      })
                    }}
                  </Button>
                  <Button
                    variant="destructive"
                    size="sm"
                    class="h-7 px-2 text-xs"
                    :disabled="!canWrite"
                    @click="removeMember(member)"
                  >
                    <Trash2 class="size-3" />
                    {{ t('admin.competitionDetail.permissionsRemove') }}
                  </Button>
                </div>
              </div>
            </div>

            <div class="space-y-2 border-t pt-3">
              <p class="text-xs font-medium">
                {{ t('admin.competitionDetail.permissionsAddMember') }}
              </p>
              <Select
                v-model="selectedCandidateIds[role]"
                :disabled="!canWrite || availableCandidates(role).length === 0"
              >
                <SelectTrigger>
                  <SelectValue
                    :placeholder="t('admin.competitionDetail.permissionsSelectCandidate')"
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="candidate in availableCandidates(role)"
                    :key="candidate.id"
                    :value="candidate.id"
                  >
                    {{ candidateOptionLabel(candidate) }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <p
                v-if="availableCandidates(role).length === 0"
                class="text-xs text-muted-foreground"
              >
                {{ t('admin.competitionDetail.permissionsNoCandidates') }}
              </p>
              <Button
                size="sm"
                class="w-full"
                :disabled="
                  !canWrite
                    || !selectedCandidateIds[role]
                    || !availableCandidates(role).some(
                      candidate => candidate.id === selectedCandidateIds[role],
                    )
                "
                @click="assignSelected(role)"
              >
                <Loader2
                  v-if="updateMutation.isPending.value"
                  class="size-4 animate-spin"
                />
                <UserRoundCog v-else class="size-4" />
                {{ t('admin.competitionDetail.permissionsAddMember') }}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </Card>
</template>
