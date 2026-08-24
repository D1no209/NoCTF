<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminExtendTeamRuntime,
  adminForceTerminateRuntime,
  adminGetRuntime,
  adminListCompetitionChallenges,
  adminListRuntimes,
  adminListTeams,
  adminResetSharedRuntime,
  adminResetTeamRuntime,
  adminStartSharedRuntime,
  adminStartTeamRuntime,
  adminTerminateRuntime,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  NoCtfapiEndpointsRuntimeRuntimeKindProtocol,
  NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { createLatestPageRefresh } from '~/lib/latest-page-refresh'
import { adminRuntimeTeamLabel } from '~/utils/admin-runtime'
import {
  createRuntimeOperationCoordinator,
  type RuntimeOperationKind,
  type RuntimeOperationToken,
} from '~/lib/runtime-operation-coordinator'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()
const { isAdministrator } = useAuth()

// ---- Reference data ----
const challengeOptions = ref<{ id: string; title: string }[]>([])
const teamOptions = ref<{ id: string; name: string }[]>([])
const runtimeTeamLabel = (
  runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null | undefined,
) => runtime
  ? adminRuntimeTeamLabel(
      runtime,
      translate,
      id => teamOptions.value.find(team => team.id === id)?.name,
    )
  : '-'
const isPlayerManagedRuntime = (
  runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
) => runtime.purpose !== 'AwdpTarget'
const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'

async function loadRefs() {
  const [challenges, teams] = await Promise.all([
    adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
    adminListTeams({ path: { competitionId } }),
  ])
  challengeOptions.value = (challenges.data?.items ?? []).map(c => ({ id: c.id!, title: c.title ?? '' }))
  teamOptions.value = (teams.data?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
}

// ---- Filters + list ----
const filterChallenge = ref('')
const filterTeam = ref('')
const filterState = ref('')
const filterKind = ref('')

const { items, loading, error: listError, hasMore, loadMore: loadRuntimePage, reset, initialized } = useCursorPagination<
  NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse
>(async (cursor) => {
  const { data, error } = await adminListRuntimes({
    path: { competitionId },
    query: {
      competitionChallengeId: filterChallenge.value || null,
      teamId: filterTeam.value || null,
      state: filterState.value === '' ? null : filterState.value as NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
      runtimeKind: filterKind.value === '' ? null : filterKind.value as NoCtfapiEndpointsRuntimeRuntimeKindProtocol,
      cursor,
      limit: 30,
    },
  })
  if (error || !data) throw parseApiError(error)
  return data
})

const {
  loadNextPage: loadMore,
  refreshLatest: refreshRuntimeList,
} = createLatestPageRefresh({
  loadMore: loadRuntimePage,
  reset: () => reset({ preserveItems: true }),
})

function applyFilters() {
  void refreshRuntimeList()
}

function refreshList() {
  return refreshRuntimeList()
}

// ---- Detail sheet ----
const detail = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const detailOpen = ref(false)
const detailLoading = ref(false)

async function openDetail(id?: string) {
  if (!id) return
  detailOpen.value = true
  detailLoading.value = true
  const { data, error } = await adminGetRuntime({ path: { competitionId, runtimeInstanceId: id } })
  if (error) toast.error(parseApiError(error).message)
  else detail.value = data ?? null
  detailLoading.value = false
}

// ---- Operations (202 + bounded background state refresh) ----
const runtimeOperations = createRuntimeOperationCoordinator()
const opMessage = ref<string | null>(null)

function runtimeOperationKey(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null): string | null {
  return rt?.id ?? null
}

function isRuntimePending(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null): boolean {
  const key = runtimeOperationKey(rt)
  return key !== null && runtimeOperations.pending.has(key)
}

function isRuntimeOperationPending(
  rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null,
  operation: RuntimeOperationKind,
): boolean {
  const key = runtimeOperationKey(rt)
  return key !== null && runtimeOperations.pending.get(key) === operation
}

function beginRuntimeOperation(
  rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  operation: RuntimeOperationKind,
): RuntimeOperationToken | null {
  const key = runtimeOperationKey(rt)
  if (!key) return null
  opMessage.value = null
  return runtimeOperations.begin(key, operation)
}

function refreshRuntimeInBackground(
  token: RuntimeOperationToken,
  runtimeInstanceId: string,
  done: (state?: string | null) => boolean,
): void {
  void runtimeOperations.poll(token, async (signal) => {
    const { data, error } = await adminGetRuntime({
      path: { competitionId, runtimeInstanceId },
      signal,
    })
    if (error || !data) return false

    if (detailOpen.value && detail.value?.id === data.id)
      detail.value = data
    await refreshList()
    return done(data.state)
  }).then((result) => {
    if (result === 'exhausted')
      opMessage.value = translate('操作已受理，状态更新较慢，请稍后刷新')
  }).finally(() => {
    runtimeOperations.finish(token)
  })
}

async function runRuntimeOp(
  rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  op: 'start' | 'reset',
) {
  if (!rt.id || !rt.competitionChallengeId) return
  const token = beginRuntimeOperation(rt, op)
  if (!token) return
  try {
    const ccPath = { competitionId, competitionChallengeId: rt.competitionChallengeId }
    const teamPath = { ...ccPath, teamId: rt.teamId ?? '' }
    const { data, error } =
      op === 'start'
        ? (rt.teamId ? await adminStartTeamRuntime({ path: teamPath }) : await adminStartSharedRuntime({ path: ccPath }))
        : (rt.teamId ? await adminResetTeamRuntime({ path: teamPath }) : await adminResetSharedRuntime({ path: ccPath }))
    if (!runtimeOperations.isActive(token)) return
    if (error) throw error
    const label = op === 'start' ? translate("启动") : translate("重置")
    toast.success(translate('{action}操作已受理', { action: label }))
    void refreshList()
    refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state =>
      op === 'reset' ? state === 'Stopped' : state === 'Running' || state === 'Failed')
  }
  catch (e) {
    const shouldNotify = runtimeOperations.isActive(token)
    runtimeOperations.finish(token)
    if (shouldNotify) toast.error(parseApiError(e).message)
  }
}

// ---- Exact termination ----
const terminateDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const terminatePending = computed(() => isRuntimeOperationPending(terminateDialog.value, 'terminate'))

function canTerminate(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
  return rt.state === 'Queued'
    || rt.state === 'Provisioning'
    || rt.state === 'Running'
    || rt.state === 'Failed'
}

async function submitTermination() {
  const rt = terminateDialog.value
  if (!rt?.id) return
  const token = beginRuntimeOperation(rt, 'terminate')
  if (!token) return
  try {
    const { data, error } = await adminTerminateRuntime({
      path: { competitionId, runtimeInstanceId: rt.id },
    })
    if (!runtimeOperations.isActive(token)) return
    if (error) throw error
    toast.success(translate("实例终止操作已受理"))
    terminateDialog.value = null
    void refreshList()
    refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state => state === 'Stopped' || state === 'Failed')
  }
  catch (e) {
    const shouldNotify = runtimeOperations.isActive(token)
    runtimeOperations.finish(token)
    if (shouldNotify) toast.error(parseApiError(e).message)
  }
}

// ---- Administrator-only stuck runtime cleanup ----
const forceTerminateDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const forceTerminateReason = ref('')
const forceTerminateConfirmed = ref(false)
const forceTerminatePending = computed(() => isRuntimeOperationPending(forceTerminateDialog.value, 'force-terminate'))

function openForceTermination(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
  forceTerminateReason.value = ''
  forceTerminateConfirmed.value = false
  forceTerminateDialog.value = rt
}

async function submitForceTermination() {
  const rt = forceTerminateDialog.value
  const reason = forceTerminateReason.value.trim()
  if (!rt?.id || reason.length < 8 || !forceTerminateConfirmed.value) return
  const token = beginRuntimeOperation(rt, 'force-terminate')
  if (!token) return
  try {
    const { data, error } = await adminForceTerminateRuntime({
      path: { competitionId, runtimeInstanceId: rt.id },
      body: {
        reason,
      },
    })
    if (!runtimeOperations.isActive(token)) return
    if (error) throw error
    toast.success(translate("强制终结已交由 Runner 清理"))
    forceTerminateDialog.value = null
    void refreshList()
    refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state => state === 'Stopped')
  }
  catch (e) {
    const shouldNotify = runtimeOperations.isActive(token)
    runtimeOperations.finish(token)
    if (shouldNotify) toast.error(parseApiError(e).message)
  }
}

// ---- Extend ----
const extendDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const extendSeconds = ref(1800)
const extendPending = computed(() => isRuntimeOperationPending(extendDialog.value, 'extend'))

async function submitExtend() {
  const rt = extendDialog.value
  if (!rt?.id || !rt.teamId || !rt.competitionChallengeId) return
  const token = beginRuntimeOperation(rt, 'extend')
  if (!token) return
  try {
    const { error } = await adminExtendTeamRuntime({
      path: { competitionId, teamId: rt.teamId, competitionChallengeId: rt.competitionChallengeId },
      body: { seconds: extendSeconds.value },
    })
    if (!runtimeOperations.isActive(token)) return
    if (error) throw error
    toast.success(translate("续期操作已受理"))
    extendDialog.value = null
    void refreshList()
    refreshRuntimeInBackground(token, rt.id, () => true)
  }
  catch (e) {
    const shouldNotify = runtimeOperations.isActive(token)
    runtimeOperations.finish(token)
    if (shouldNotify) toast.error(parseApiError(e).message)
  }
}

onMounted(() => {
  void loadRefs()
  void loadMore()
})

onBeforeUnmount(() => runtimeOperations.cancelAll())
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardContent class="flex flex-wrap items-center gap-3 pt-6">
        <Select v-model="filterChallenge">
          <SelectTrigger class="w-48">
            <SelectValue :placeholder="$t('题目(全部)')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterTeam">
          <SelectTrigger class="w-48">
            <SelectValue :placeholder="$t('队伍(全部)')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterState">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('状态(全部)')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Queued">{{ $t('排队中') }}</SelectItem>
              <SelectItem value="Provisioning">{{ $t('准备中') }}</SelectItem>
              <SelectItem value="Running">{{ $t('运行中') }}</SelectItem>
              <SelectItem value="Stopping">{{ $t('停止中') }}</SelectItem>
              <SelectItem value="Stopped">{{ $t('已停止') }}</SelectItem>
              <SelectItem value="Failed">{{ $t('失败') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterKind">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('类型(全部)')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Container">{{ $t('容器') }}</SelectItem>
              <SelectItem value="Compose">Compose</SelectItem>
              <SelectItem value="OvaVm">{{ $t('虚拟机') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Button size="sm" @click="applyFilters">{{ $t('应用筛选') }}</Button>
        <Button variant="ghost" size="sm" @click="filterChallenge = ''; filterTeam = ''; filterState = ''; filterKind = ''; applyFilters()"> {{ $t('清空') }} </Button>
      </CardContent>
    </Card>

    <Alert v-if="opMessage">
      <AlertDescription>{{ opMessage }}</AlertDescription>
    </Alert>

    <Alert v-if="listError" variant="destructive">
      <AlertDescription>{{ listError.message }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('没有符合条件的运行时实例') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('队伍') }}</TableHead>
            <TableHead>{{ $t('题目') }}</TableHead>
            <TableHead class="w-24">{{ $t('类型') }}</TableHead>
            <TableHead class="w-24">{{ $t('状态') }}</TableHead>
            <TableHead class="w-44">{{ $t('到期时间') }}</TableHead>
            <TableHead class="w-64 text-right">{{ $t('操作') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="rt in items" :key="rt.id">
            <TableCell class="font-medium">{{ runtimeTeamLabel(rt) }}</TableCell>
            <TableCell>{{ challengeTitle(rt.competitionChallengeId) }}</TableCell>
            <TableCell>{{ enumLabel(RuntimeKindLabel, rt.runtimeKind) }}</TableCell>
            <TableCell>
              <Badge :variant="rt.state === 'Running' ? 'default' : rt.state === 'Failed' ? 'destructive' : 'secondary'">
                {{ enumLabel(RuntimeStateLabel, rt.state) }}
              </Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(rt.expiresAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(rt.id)">{{ $t('详情') }}</Button>
                <template v-if="canWrite">
                  <Button
                    v-if="isPlayerManagedRuntime(rt) && (rt.state === 'Stopped' || rt.state === 'Failed')"
                    variant="ghost" size="sm" :disabled="isRuntimePending(rt)"
                    @click="runRuntimeOp(rt, 'start')"
                  >
                    <Spinner v-if="isRuntimeOperationPending(rt, 'start')" data-icon="inline-start" /> {{ $t('启动') }} </Button>
                  <Button
                    v-if="canTerminate(rt)"
                    variant="destructive" size="sm" :disabled="isRuntimePending(rt)"
                    @click="terminateDialog = rt"
                  >{{ $t('终止') }}</Button>
                  <Button
                    v-if="isAdministrator && rt.canForceTerminate"
                    variant="destructive" size="sm"
                    :disabled="isRuntimePending(rt)"
                    @click="openForceTermination(rt)"
                  >{{ $t('强制终结') }}</Button>
                  <Button
                    v-if="isPlayerManagedRuntime(rt)"
                    variant="ghost" size="sm" :disabled="isRuntimePending(rt)"
                    @click="runRuntimeOp(rt, 'reset')"
                  >
                    <Spinner v-if="isRuntimeOperationPending(rt, 'reset')" data-icon="inline-start" /> {{ $t('重置') }} </Button>
                  <Button
                    v-if="isPlayerManagedRuntime(rt) && rt.teamId && rt.state === 'Running'"
                    variant="ghost" size="sm" :disabled="isRuntimePending(rt)"
                    @click="extendDialog = rt; extendSeconds = 1800"
                  >{{ $t('续期') }}</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMore">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('加载更多') }} </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('运行时详情') }}</SheetTitle>
          <SheetDescription class="font-mono text-xs break-all">{{ detail?.id }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('队伍') }}</span><span>{{ runtimeTeamLabel(detail) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('题目') }}</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('类型') }}</span><span>{{ enumLabel(RuntimeKindLabel, detail.runtimeKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">Provider</span><span>{{ enumLabel(RuntimeProviderLabel, detail.provider) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">Runner</span><span>{{ detail.runnerId ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('状态') }}</span><span>{{ enumLabel(RuntimeStateLabel, detail.state) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('失败代码') }}</span><span>{{ detail.failureCode ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('创建时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.createdAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('运行时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.runningAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('到期时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.expiresAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('停止时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.stoppedAt) }}</span></div>
          <template v-if="detail.urls?.length">
            <Separator />
            <p class="text-muted-foreground">{{ $t('访问地址') }}</p>
            <RuntimeAccessUrl v-for="url in detail.urls" :key="url" :url="url" />
          </template>
          <template v-if="detail.publishedPorts?.length">
            <Separator />
            <p class="text-muted-foreground">{{ $t('端口映射') }}</p>
            <div v-for="(p, i) in detail.publishedPorts" :key="i" class="font-mono text-xs">
              {{ $t('{service}:{containerPort} → 主机 {hostPort}', { service: p.serviceName ?? 'service', containerPort: p.containerPort ?? '-', hostPort: p.hostPort ?? '-' }) }}
            </div>
          </template>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="extendDialog !== null" @update:open="(v) => { if (!v) extendDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('延长运行时间') }}</DialogTitle>
          <DialogDescription>{{ $t('为「{team}」的实例延长到期时间', { team: runtimeTeamLabel(extendDialog) }) }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="extend-seconds">{{ $t('延长秒数') }}</FieldLabel>
            <Input id="extend-seconds" v-model.number="extendSeconds" type="number" min="60" step="60" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="extendDialog = null">{{ $t('取消') }}</Button>
          <Button :disabled="extendPending" @click="submitExtend">
            <Spinner v-if="extendPending" data-icon="inline-start" /> {{ $t('确认续期') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="terminateDialog !== null" @update:open="(open) => { if (!open && !terminatePending) terminateDialog = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('终止此运行时实例？') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('将立即停止并清理「{team}」在「{challenge}」的实例。该操作不会重建环境。', {
              team: runtimeTeamLabel(terminateDialog),
              challenge: challengeTitle(terminateDialog?.competitionChallengeId),
            }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">{{ $t('取消') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="terminatePending"
            @click="submitTermination"
          >
            <Spinner v-if="terminatePending" data-icon="inline-start" /> {{ $t('确认终止') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog
      :open="forceTerminateDialog !== null"
      @update:open="(open) => { if (!open && !forceTerminatePending) forceTerminateDialog = null }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('强制终结卡住的实例？') }}</AlertDialogTitle>
          <AlertDialogDescription> {{ $t('Runner 将按实例 ID 与资源标签清理实际资源，并在确认资源不存在、容量已释放后才把实例标记为 Stopped。 该操作只用于长时间停在 Provisioning 或 Stopping 的实例。') }} </AlertDialogDescription>
        </AlertDialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="force-termination-reason">{{ $t('操作原因') }}</FieldLabel>
            <Textarea
              id="force-termination-reason"
              v-model="forceTerminateReason"
              maxlength="512"
              :placeholder="$t('至少 8 个字符；将写入不可变比赛审计')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}/512</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="force-termination-confirm" v-model="forceTerminateConfirmed" />
            <FieldLabel for="force-termination-confirm" class="font-normal"> {{ $t('我确认这是卡住的实例，并理解清理失败时实例不会被强改为 Stopped。') }} </FieldLabel>
          </Field>
        </FieldGroup>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="forceTerminatePending">{{ $t('取消') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="forceTerminatePending || !forceTerminateConfirmed || forceTerminateReason.trim().length < 8"
            @click="submitForceTermination"
          >
            <Spinner v-if="forceTerminatePending" data-icon="inline-start" /> {{ $t('确认强制终结') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
