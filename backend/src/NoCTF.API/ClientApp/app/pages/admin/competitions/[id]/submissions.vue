<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAccessCompetitionGameplayFactValue,
  adminGetGameplayFact,
  adminListCompetitionChallenges,
  adminListGameplayFacts,
  adminListTeams,
  adminQueueGameplayFactEvaluation,
  adminRejudgeGameplayFact,
  adminRejudgeGameplayFacts,
} from '~/api'
import type {
  NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

interface FilterOption<T extends string> {
  value: T
  label: string
}

const gameplayFactKindOptions = [
  { value: 'FlagAttempt', label: 'Flag' },
  { value: 'BreakAttempt', label: 'Break' },
  { value: 'FixAttempt', label: 'Fix' },
] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol>[]

const gameplayFactStateOptions = [
  { value: 'Pending', label: '待处理' },
  { value: 'Queued', label: '排队中' },
  { value: 'Processing', label: '评测中' },
  { value: 'Completed', label: '已完成' },
  { value: 'PlatformFailed', label: '平台失败' },
] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol>[]

const gameplayFactResultOptions = [
  { value: 'Correct', label: '正确' },
  { value: 'Wrong', label: '错误' },
  { value: 'Duplicate', label: '重复' },
  { value: 'AttemptsExhausted', label: '次数耗尽' },
  { value: 'Rejected', label: '已拒绝' },
] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol>[]

// ---- Reference data for filters ----
const challengeOptions = ref<{ id: string; title: string }[]>([])
const teamOptions = ref<{ id: string; name: string }[]>([])
const teamName = (id?: string | null) => teamOptions.value.find(t => t.id === id)?.name ?? id ?? '-'
const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'

async function loadRefs() {
  const [challenges, teams] = await Promise.all([
    adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
    adminListTeams({ path: { competitionId } }),
  ])
  challengeOptions.value = (challenges.data?.items ?? [])
    .map(c => ({ id: c.id!, title: c.title ?? '' }))
  teamOptions.value = (teams.data?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
}

// ---- Filters + paged list ----
const filterChallenge = ref('')
const filterTeam = ref('')
const filterKind = ref<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol | ''>('')
const filterState = ref<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | ''>('')
const filterResult = ref<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | ''>('')
const filterFlag = ref('')

const { items, loading, error: listError, hasMore, loadMore, reset, initialized } = useCursorPagination<
  NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse
>(async (cursor) => {
  const { data, error } = await adminListGameplayFacts({
    path: { competitionId },
    query: {
      competitionChallengeId: filterChallenge.value || null,
      teamId: filterTeam.value || null,
      gameplayFactKind: filterKind.value || null,
      state: filterState.value || null,
      gameplayFactResult: filterResult.value || null,
      value: filterFlag.value || null,
      cursor,
      limit: 30,
    },
  })
  if (error || !data) throw parseApiError(error)
  return data
})

function applyFilters() {
  reset({ preserveItems: true })
  void loadMore()
}

// ---- Detail sheet ----
const detail = ref<NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse | null>(null)
const detailOpen = ref(false)
const detailLoading = ref(false)

async function openDetail(id?: string) {
  if (!id) return
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  const { data, error } = await adminGetGameplayFact({ path: { competitionId, gameplayFactId: id } })
  if (error) toast.error(parseApiError(error).message)
  else detail.value = data ?? null
  detailLoading.value = false
}

// ---- Rejudge / queue evaluation ----
const actionPending = ref<string | null>(null)

async function rejudgeOne(gameplayFactId?: string) {
  if (!gameplayFactId) return
  actionPending.value = gameplayFactId
  try {
    const { error } = await adminRejudgeGameplayFact({ path: { competitionId, gameplayFactId } })
    if (error) throw error
    toast.success(translate("已加入重判队列"))
    applyFilters()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    actionPending.value = null
  }
}

const batchTarget = ref<string>('')

async function rejudgeBatch() {
  if (!batchTarget.value) return
  actionPending.value = 'batch'
  try {
    const { error } = await adminRejudgeGameplayFacts({
      path: { competitionId },
      body: { competitionChallengeId: batchTarget.value },
    })
    if (error) throw error
    toast.success(translate("已将题目全部提交加入重判队列"))
    applyFilters()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    actionPending.value = null
  }
}

async function queueEvaluation() {
  if (!batchTarget.value) return
  actionPending.value = 'queue'
  try {
    const { error } = await adminQueueGameplayFactEvaluation({
      path: { competitionId },
      body: { competitionChallengeId: batchTarget.value },
    })
    if (error) throw error
    toast.success(translate("已触发评测队列"))
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    actionPending.value = null
  }
}

// ---- Protected flag access (audited) ----
const flagDialog = ref<{ gameplayFactId: string } | null>(null)
const flagReason = ref('')
const flagResult = ref<string | null>(null)
const flagPending = ref(false)

function openFlagAccess(gameplayFactId?: string) {
  if (!gameplayFactId) return
  flagDialog.value = { gameplayFactId }
  flagReason.value = ''
  flagResult.value = null
}

async function accessFlag() {
  const ctx = flagDialog.value
  if (!ctx || !flagReason.value.trim()) return
  flagPending.value = true
  try {
    const { data, error } = await adminAccessCompetitionGameplayFactValue({
      path: { competitionId, gameplayFactId: ctx.gameplayFactId },
      body: { reason: flagReason.value.trim() },
    })
    if (error) throw error
    flagResult.value = data?.value ?? translate('(无内容)')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    flagPending.value = false
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
      <CardContent class="flex flex-col gap-3 pt-6">
        <div class="grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <Select v-model="filterChallenge">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('题目(全部)')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterTeam">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('队伍(全部)')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterKind">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('类型(全部)')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactKindOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterState">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('评测状态(全部)')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactStateOptions" :key="option.value" :value="option.value">
                  {{ $t(option.label) }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterResult">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('结果(全部)')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactResultOptions" :key="option.value" :value="option.value">
                  {{ $t(option.label) }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Input v-model="filterFlag" :placeholder="$t('提交的 Flag 包含…')" />
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <Button size="sm" @click="applyFilters">{{ $t('应用筛选') }}</Button>
          <Button
            variant="ghost"
            size="sm"
            @click="filterChallenge = ''; filterTeam = ''; filterKind = ''; filterState = ''; filterResult = ''; filterFlag = ''; applyFilters()"
          > {{ $t('清空') }} </Button>
          <template v-if="canWrite">
            <Separator orientation="vertical" class="h-6" />
            <Select v-model="batchTarget">
              <SelectTrigger class="w-48">
                <SelectValue :placeholder="$t('选择题目(批量操作)')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="rejudgeBatch">
              <Spinner v-if="actionPending === 'batch'" data-icon="inline-start" /> {{ $t('整题重判') }} </Button>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="queueEvaluation">
              <Spinner v-if="actionPending === 'queue'" data-icon="inline-start" /> {{ $t('触发评测') }} </Button>
          </template>
        </div>
      </CardContent>
    </Card>

    <Alert v-if="listError" variant="destructive">
      <AlertDescription>{{ listError.message }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('没有符合条件的提交') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('队伍') }}</TableHead>
            <TableHead>{{ $t('题目') }}</TableHead>
            <TableHead class="w-20">{{ $t('类型') }}</TableHead>
            <TableHead class="w-24">{{ $t('评测状态') }}</TableHead>
            <TableHead class="w-24">{{ $t('结果') }}</TableHead>
            <TableHead class="w-44">{{ $t('提交时间') }}</TableHead>
            <TableHead class="w-52 text-right">{{ $t('操作') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="s in items" :key="s.id">
            <TableCell class="font-medium">{{ teamName(s.teamId) }}</TableCell>
            <TableCell>{{ challengeTitle(s.competitionChallengeId) }}</TableCell>
            <TableCell>{{ enumLabel(GameplayFactKindLabel, s.kind) }}</TableCell>
            <TableCell>
              <Badge :variant="s.state === 'Completed' ? 'default' : s.state === 'PlatformFailed' ? 'destructive' : 'secondary'">
                {{ enumLabel(GameplayFactStateLabel, s.state) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="s.result !== null && s.result !== undefined" :variant="s.result === 'Correct' ? 'default' : 'outline'">
                {{ enumLabel(GameplayFactResultLabel, s.result) }}
              </Badge>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(s.occurredAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(s.id)">{{ $t('详情') }}</Button>
                <Button variant="ghost" size="sm" @click="openFlagAccess(s.id)">{{ $t('读取 Flag') }}</Button>
                <Button v-if="canWrite" variant="ghost" size="sm" :disabled="actionPending === s.id" @click="rejudgeOne(s.id)">
                  <Spinner v-if="actionPending === s.id" data-icon="inline-start" /> {{ $t('重判') }} </Button>
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
          <SheetTitle>{{ $t('提交详情') }}</SheetTitle>
          <SheetDescription>{{ $t('提交 ID：{id}', { id: detail?.gameplayFactId ?? '-' }) }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('队伍') }}</span><span>{{ teamName(detail.teamId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('题目') }}</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('行为人') }}</span><span class="font-mono text-xs">{{ detail.actorUserId ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('类型') }}</span><span>{{ enumLabel(GameplayFactKindLabel, detail.kind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('评测状态') }}</span><span>{{ enumLabel(GameplayFactStateLabel, detail.state) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('结果') }}</span><span>{{ detail.result !== null && detail.result !== undefined ? enumLabel(GameplayFactResultLabel, detail.result) : '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('失败代码') }}</span><span>{{ detail.failureCode ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('提交时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.occurredAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('更新时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.updatedAt) }}</span></div>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="flagDialog !== null" @update:open="(v) => { if (!v) flagDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('读取受保护的提交 Flag') }}</DialogTitle>
          <DialogDescription>{{ $t('该操作会被记录审计日志,请填写读取原因。') }}</DialogDescription>
        </DialogHeader>
        <template v-if="flagResult === null">
          <FieldGroup>
            <Field>
              <FieldLabel for="flag-reason">{{ $t('读取原因') }}</FieldLabel>
              <Textarea id="flag-reason" v-model="flagReason" required />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="flagDialog = null">{{ $t('取消') }}</Button>
            <Button :disabled="flagPending || !flagReason.trim()" @click="accessFlag">
              <Spinner v-if="flagPending" data-icon="inline-start" /> {{ $t('读取') }} </Button>
          </DialogFooter>
        </template>
        <template v-else>
          <div class="rounded-md border bg-muted p-3 font-mono text-sm break-all">{{ flagResult }}</div>
          <DialogFooter>
            <Button @click="flagDialog = null">{{ $t('关闭') }}</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
  </div>
</template>
