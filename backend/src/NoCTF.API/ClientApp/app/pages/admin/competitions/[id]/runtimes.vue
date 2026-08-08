<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminExtendTeamRuntime,
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

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

// ---- Reference data ----
const challengeOptions = ref<{ id: string; title: string }[]>([])
const teamOptions = ref<{ id: string; name: string }[]>([])
const teamName = (id?: string | null) => (id ? (teamOptions.value.find(t => t.id === id)?.name ?? id) : '共享')
const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '—'

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

const { items, loading, hasMore, loadMore, reset, initialized } = useCursorPagination<
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
  if (error) throw error
  return data ?? {}
})

function applyFilters() {
  reset()
  void loadMore()
}

function refreshList() {
  reset()
  void loadMore()
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

// ---- Operations (202 + poll runtime state) ----
const opPending = ref<string | null>(null)
const opMessage = ref<string | null>(null)

async function waitForRuntime(runtimeInstanceId: string | undefined, done: (state?: string | null) => boolean) {
  if (!runtimeInstanceId) return
  let attempts = 0
  while (attempts < 30) {
    attempts += 1
    try {
      const { data } = await adminGetRuntime({ path: { competitionId, runtimeInstanceId } })
      if (done(data?.state)) return
    }
    catch { /* transient */ }
    await new Promise(r => setTimeout(r, Math.min(1000 * attempts, 5000)))
  }
  opMessage.value = '操作已受理,状态更新较慢,请稍后刷新'
}

async function runRuntimeOp(
  rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  op: 'start' | 'reset',
) {
  if (!rt.competitionChallengeId) return
  opPending.value = `${rt.id}:${op}`
  opMessage.value = null
  try {
    const ccPath = { competitionId, competitionChallengeId: rt.competitionChallengeId }
    const teamPath = { ...ccPath, teamId: rt.teamId ?? '' }
    const { data, error } =
      op === 'start'
        ? (rt.teamId ? await adminStartTeamRuntime({ path: teamPath }) : await adminStartSharedRuntime({ path: ccPath }))
        : (rt.teamId ? await adminResetTeamRuntime({ path: teamPath }) : await adminResetSharedRuntime({ path: ccPath }))
    if (error) throw error
    const label = op === 'start' ? '启动' : '重置'
    toast.success(`${label}操作已受理`)
    await waitForRuntime(data?.runtimeInstanceId ?? rt.id, state =>
      op === 'reset' ? state === 'Stopped' : state === 'Running' || state === 'Failed')
    refreshList()
    if (detailOpen.value && detail.value?.id) await openDetail(detail.value.id)
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    opPending.value = null
  }
}

// ---- Exact termination ----
const terminateDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const terminatePending = ref(false)

function canTerminate(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
  return rt.state === 'Queued'
    || rt.state === 'Provisioning'
    || rt.state === 'Running'
    || (rt.state === 'Failed' && Boolean(rt.providerReceiptJson))
}

async function submitTermination() {
  const rt = terminateDialog.value
  if (!rt?.id || rt.processingVersion === undefined || rt.processingVersion === null) return
  terminatePending.value = true
  try {
    const { data, error } = await adminTerminateRuntime({
      path: { competitionId, runtimeInstanceId: rt.id },
      body: { expectedProcessingVersion: rt.processingVersion },
    })
    if (error) throw error
    toast.success('实例终止操作已受理')
    terminateDialog.value = null
    await waitForRuntime(data?.runtimeInstanceId ?? rt.id, state => state === 'Stopped' || state === 'Failed')
    refreshList()
    if (detailOpen.value && detail.value?.id === rt.id) await openDetail(rt.id)
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    terminatePending.value = false
  }
}

// ---- Extend ----
const extendDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)
const extendSeconds = ref(1800)
const extendPending = ref(false)

async function submitExtend() {
  const rt = extendDialog.value
  if (!rt?.teamId || !rt.competitionChallengeId) return
  extendPending.value = true
  try {
    const { error } = await adminExtendTeamRuntime({
      path: { competitionId, teamId: rt.teamId, competitionChallengeId: rt.competitionChallengeId },
      body: { seconds: extendSeconds.value },
    })
    if (error) throw error
    toast.success('续期操作已受理')
    extendDialog.value = null
    await waitForRuntime(rt.id, () => true)
    refreshList()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    extendPending.value = false
  }
}

onMounted(() => {
  void loadRefs()
  void loadMore()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardContent class="flex flex-wrap items-center gap-3 pt-6">
        <Select v-model="filterChallenge">
          <SelectTrigger class="w-48">
            <SelectValue placeholder="题目(全部)" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterTeam">
          <SelectTrigger class="w-48">
            <SelectValue placeholder="队伍(全部)" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterState">
          <SelectTrigger class="w-40">
            <SelectValue placeholder="状态(全部)" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Queued">排队中</SelectItem>
              <SelectItem value="Provisioning">准备中</SelectItem>
              <SelectItem value="Running">运行中</SelectItem>
              <SelectItem value="Stopping">停止中</SelectItem>
              <SelectItem value="Stopped">已停止</SelectItem>
              <SelectItem value="Failed">失败</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterKind">
          <SelectTrigger class="w-40">
            <SelectValue placeholder="类型(全部)" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Container">容器</SelectItem>
              <SelectItem value="Compose">Compose</SelectItem>
              <SelectItem value="OvaVm">虚拟机</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Button size="sm" @click="applyFilters">应用筛选</Button>
        <Button variant="ghost" size="sm" @click="filterChallenge = ''; filterTeam = ''; filterState = ''; filterKind = ''; applyFilters()">
          清空
        </Button>
      </CardContent>
    </Card>

    <Alert v-if="opMessage">
      <AlertDescription>{{ opMessage }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && items.length === 0">
      <EmptyHeader>
        <EmptyTitle>没有符合条件的运行时实例</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>队伍</TableHead>
            <TableHead>题目</TableHead>
            <TableHead class="w-20">代数</TableHead>
            <TableHead class="w-24">类型</TableHead>
            <TableHead class="w-24">状态</TableHead>
            <TableHead class="w-44">到期时间</TableHead>
            <TableHead class="w-64 text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="rt in items" :key="rt.id">
            <TableCell class="font-medium">{{ teamName(rt.teamId) }}</TableCell>
            <TableCell>{{ challengeTitle(rt.competitionChallengeId) }}</TableCell>
            <TableCell>{{ rt.generation }}</TableCell>
            <TableCell>{{ enumLabel(RuntimeKindLabel, rt.runtimeKind) }}</TableCell>
            <TableCell>
              <Badge :variant="rt.state === 'Running' ? 'default' : rt.state === 'Failed' ? 'destructive' : 'secondary'">
                {{ enumLabel(RuntimeStateLabel, rt.state) }}
              </Badge>
            </TableCell>
            <TableCell>{{ adminFormatDateTime(rt.expiresAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(rt.id)">详情</Button>
                <template v-if="canWrite">
                  <Button
                    v-if="rt.state === 'Stopped' || rt.state === 'Failed'"
                    variant="ghost" size="sm" :disabled="opPending !== null"
                    @click="runRuntimeOp(rt, 'start')"
                  >启动</Button>
                  <Button
                    v-if="canTerminate(rt)"
                    variant="destructive" size="sm" :disabled="opPending !== null || terminatePending"
                    @click="terminateDialog = rt"
                  >终止</Button>
                  <Button
                    variant="ghost" size="sm" :disabled="opPending !== null"
                    @click="runRuntimeOp(rt, 'reset')"
                  >
                    <Spinner v-if="opPending === `${rt.id}:reset`" data-icon="inline-start" />
                    重置
                  </Button>
                  <Button
                    v-if="rt.teamId && rt.state === 'Running'"
                    variant="ghost" size="sm" :disabled="opPending !== null"
                    @click="extendDialog = rt; extendSeconds = 1800"
                  >续期</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMore">
          <Spinner v-if="loading" data-icon="inline-start" />
          加载更多
        </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>运行时详情</SheetTitle>
          <SheetDescription class="font-mono text-xs break-all">{{ detail?.id }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">队伍</span><span>{{ teamName(detail.teamId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">题目</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">代数</span><span>{{ detail.generation }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">类型</span><span>{{ enumLabel(RuntimeKindLabel, detail.runtimeKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">Provider</span><span>{{ enumLabel(RuntimeProviderLabel, detail.provider) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">Runner</span><span>{{ detail.runnerPool }}{{ detail.runnerId ? ` / ${detail.runnerId}` : '' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">状态</span><span>{{ enumLabel(RuntimeStateLabel, detail.state) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">失败代码</span><span>{{ detail.failureCode ?? '—' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">创建时间</span><span>{{ adminFormatDateTime(detail.createdAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">运行时间</span><span>{{ adminFormatDateTime(detail.runningAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">到期时间</span><span>{{ adminFormatDateTime(detail.expiresAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">停止时间</span><span>{{ adminFormatDateTime(detail.stoppedAt) }}</span></div>
          <template v-if="detail.urls?.length">
            <Separator />
            <p class="text-muted-foreground">访问地址</p>
            <a v-for="u in detail.urls" :key="u" :href="u" target="_blank" rel="noopener" class="font-mono text-xs underline break-all">{{ u }}</a>
          </template>
          <template v-if="detail.publishedPorts?.length">
            <Separator />
            <p class="text-muted-foreground">端口映射</p>
            <div v-for="(p, i) in detail.publishedPorts" :key="i" class="font-mono text-xs">
              {{ p.serviceName ?? 'service' }}:{{ p.containerPort }} → 主机 {{ p.hostPort }}
            </div>
          </template>
          <template v-if="detail.providerReceiptJson">
            <Separator />
            <p class="text-muted-foreground">Provider 回执</p>
            <pre class="max-h-64 overflow-auto rounded-md border bg-muted p-2 font-mono text-xs">{{ detail.providerReceiptJson }}</pre>
          </template>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="extendDialog !== null" @update:open="(v) => { if (!v) extendDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>延长运行时间</DialogTitle>
          <DialogDescription>为「{{ teamName(extendDialog?.teamId) }}」的实例延长到期时间</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="extend-seconds">延长秒数</FieldLabel>
            <Input id="extend-seconds" v-model.number="extendSeconds" type="number" min="60" step="60" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="extendDialog = null">取消</Button>
          <Button :disabled="extendPending" @click="submitExtend">
            <Spinner v-if="extendPending" data-icon="inline-start" />
            确认续期
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="terminateDialog !== null" @update:open="(open) => { if (!open && !terminatePending) terminateDialog = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>终止此运行时实例？</AlertDialogTitle>
          <AlertDialogDescription>
            将立即停止并清理「{{ teamName(terminateDialog?.teamId) }}」在
            「{{ challengeTitle(terminateDialog?.competitionChallengeId) }}」的第
            {{ terminateDialog?.generation }} 代实例。该操作不会重建环境。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">取消</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="terminatePending" @click="submitTermination">
            <Spinner v-if="terminatePending" data-icon="inline-start" />
            确认终止
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
