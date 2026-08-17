<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAccessCompetitionGameplayFactValue,
  adminGetGameplayFact,
  adminListCompetitionChallenges,
  adminListGameplayFacts,
  adminPreviewHistoricalAdjudicationDifferences,
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
  NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse,
  NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite, canJudge } = useCompetitionAdmin()
const { isAdministrator } = useAuth()

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

// ---- Read-only historical adjudication difference preview ----
const previewItems = ref<NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse[]>([])
const previewCursor = ref<string | null>(null)
const previewLoading = ref(false)
const previewError = ref<string | null>(null)
const previewInitialized = ref(false)
let previewGeneration = 0

const differenceLabels: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol, string> = {
  CurrentCorrectShouldBeDuplicate: '当前正确结果按权威顺序应为重复',
  CurrentDuplicateShouldBeCorrect: '当前重复结果来自旧版错误，应恢复为正确',
  DuplicateWithoutCurrentPredecessor: '当前重复结果缺少仍为正确的前序事实',
  HistoricalResultChanged: '历史裁决与当前结果不一致或曾发生变化',
  MissingAdjudicationRecord: '当前结果缺少不可变裁决事件',
  TeamEligibilityHistoryRequiresReview: '队伍当前资格无法证明发生时血榜资格',
  MissingBloodAward: '缺少确定应有的血榜奖励',
  UnexpectedBloodAward: '存在当前结果无法支持的血榜奖励',
  WrongBloodRank: '记录的血榜名次与权威顺序不一致',
  DuplicateBloodAward: '同一提交存在重复血榜奖励',
}

const bloodRankLabel = (rank?: string | null) => rank === 'First'
  ? translate('一血')
  : rank === 'Second'
    ? translate('二血')
    : rank === 'Third'
      ? translate('三血')
      : '-'

async function loadPreview(reset = false) {
  if (previewLoading.value && !reset) return
  if (reset) {
    previewGeneration++
    previewItems.value = []
    previewCursor.value = null
  }
  const generation = previewGeneration
  const cursor = previewCursor.value
  previewLoading.value = true
  previewError.value = null
  try {
    const { data, error } = await adminPreviewHistoricalAdjudicationDifferences({
      path: { competitionId },
      query: {
        competitionChallengeId: filterChallenge.value || null,
        cursor,
        limit: 30,
      },
    })
    if (error || !data) throw parseApiError(error)
    if (generation !== previewGeneration) return
    previewItems.value.push(...(data.items ?? []))
    previewCursor.value = data.nextCursor ?? null
  }
  catch (requestError) {
    if (generation !== previewGeneration) return
    previewError.value = parseApiError(requestError, translate('加载历史裁决差异失败')).message
  }
  finally {
    if (generation === previewGeneration) {
      previewLoading.value = false
      previewInitialized.value = true
    }
  }
}

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
  void loadPreview(true)
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

// ---- Protected flag access ----
const flagDialog = ref<{ gameplayFactId: string } | null>(null)
const flagResult = ref<string | null>(null)
const flagError = ref<string | null>(null)
const flagPending = ref(false)
let flagRequestSequence = 0

function openFlagAccess(gameplayFactId?: string) {
  if (!gameplayFactId) return
  const requestSequence = ++flagRequestSequence
  flagDialog.value = { gameplayFactId }
  flagResult.value = null
  flagError.value = null
  flagPending.value = false
  void accessFlag(requestSequence)
}

function closeFlagAccess() {
  flagRequestSequence++
  flagDialog.value = null
  flagPending.value = false
}

async function accessFlag(requestSequence = ++flagRequestSequence) {
  const ctx = flagDialog.value
  if (!ctx) return
  flagPending.value = true
  flagError.value = null
  try {
    const { data, error } = await adminAccessCompetitionGameplayFactValue({
      path: { competitionId, gameplayFactId: ctx.gameplayFactId },
    })
    if (error) throw error
    if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
    flagResult.value = data?.value ?? translate('(无内容)')
  }
  catch (e) {
    if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
    flagError.value = parseApiError(e).message
    toast.error(flagError.value)
  }
  finally {
    if (requestSequence === flagRequestSequence)
      flagPending.value = false
  }
}

onMounted(() => {
  void loadRefs()
  void loadMore()
  void loadPreview(true)
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardHeader class="flex flex-row items-start justify-between gap-4">
        <div class="space-y-1">
          <CardTitle>{{ $t('历史裁决差异预览') }}</CardTitle>
          <CardDescription>{{ $t('当前分析 CTF Flag，并识别旧版本错误判为重复成就的 AWDP Break；AWD、KoH 与其他事实类型不在此预览中。只读分析不会重判、纠正或改写任何记录。') }}</CardDescription>
        </div>
        <Button variant="outline" size="sm" :disabled="previewLoading" @click="loadPreview(true)">
          <Spinner v-if="previewLoading" data-icon="inline-start" /> {{ $t('重新分析') }}
        </Button>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <Alert v-if="previewError" variant="destructive">
          <AlertDescription>{{ previewError }}</AlertDescription>
        </Alert>
        <Skeleton v-else-if="previewLoading && !previewInitialized" class="h-24 w-full" />
        <Alert v-else-if="previewInitialized && previewItems.length === 0">
          <AlertDescription>{{ $t('当前扫描范围内未发现裁决或血榜差异。') }}</AlertDescription>
        </Alert>
        <div v-for="item in previewItems" :key="item.gameplayFactId" class="rounded-lg border p-4">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
              <p class="font-medium">{{ item.challengeTitle }} · {{ item.teamName ?? '-' }}</p>
              <p class="mt-1 font-mono text-xs text-muted-foreground">{{ item.gameplayFactId }} · {{ adminFormatDateTime(item.occurredAt) }}</p>
            </div>
            <div class="flex flex-wrap gap-1">
              <Badge v-for="difference in item.differences" :key="`${difference.kind}-${difference.certainty}`" :variant="difference.certainty === 'Deterministic' ? 'destructive' : 'secondary'">
                {{ difference.certainty === 'Deterministic' ? $t('确定性差异') : $t('需人工复核') }}
              </Badge>
            </div>
          </div>
          <div class="mt-3 grid gap-2 text-sm sm:grid-cols-3">
            <p><span class="text-muted-foreground">{{ $t('当前结果') }}：</span>{{ item.currentResult ? enumLabel(GameplayFactResultLabel, item.currentResult) : '-' }}</p>
            <p><span class="text-muted-foreground">{{ $t('确定性预期') }}：</span>{{ item.deterministicExpectedResult ? enumLabel(GameplayFactResultLabel, item.deterministicExpectedResult) : '-' }}</p>
            <p><span class="text-muted-foreground">{{ $t('血榜记录') }}：</span>{{ item.recordedBloodRanks?.map(bloodRankLabel).join('、') || '-' }}</p>
          </div>
          <ul class="mt-3 list-disc space-y-1 pl-5 text-sm">
            <li v-for="difference in item.differences" :key="difference.kind">
              {{ $t(differenceLabels[difference.kind!]) }}
            </li>
          </ul>
        </div>
        <Button v-if="previewCursor" variant="outline" :disabled="previewLoading" @click="loadPreview(false)">
          <Spinner v-if="previewLoading" data-icon="inline-start" /> {{ $t('继续扫描更早记录') }}
        </Button>
      </CardContent>
    </Card>

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
              <div v-if="s.result !== null && s.result !== undefined || s.failureCode" class="flex flex-col items-start gap-1">
                <Badge v-if="s.result !== null && s.result !== undefined" :variant="s.result === 'Correct' ? 'default' : 'outline'">
                  {{ enumLabel(GameplayFactResultLabel, s.result) }}
                </Badge>
                <span v-if="s.failureCode" class="text-xs text-muted-foreground">
                  {{ gameplayFactFailureCodeLabel(s.failureCode) }}
                </span>
              </div>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(s.occurredAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(s.id)">{{ $t('详情') }}</Button>
                <Button v-if="canJudge" variant="ghost" size="sm" @click="openFlagAccess(s.id)">{{ $t('读取 Flag') }}</Button>
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
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('失败原因') }}</span><span>{{ gameplayFactFailureCodeLabel(detail.failureCode) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('提交时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.occurredAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('更新时间') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.updatedAt) }}</span></div>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="flagDialog !== null" @update:open="(v) => { if (!v) closeFlagAccess() }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('读取受保护的提交 Flag') }}</DialogTitle>
          <DialogDescription>
            {{ isAdministrator
              ? $t('平台管理员读取 Flag 不记录审计日志。')
              : $t('比赛工作人员读取 Flag 会记录审计日志。') }}
          </DialogDescription>
        </DialogHeader>
        <div v-if="flagPending" class="flex min-h-24 items-center justify-center">
          <Spinner class="size-5" />
        </div>
        <Alert v-else-if="flagError" variant="destructive">
          <AlertTitle>{{ $t('读取 Flag 失败') }}</AlertTitle>
          <AlertDescription>{{ flagError }}</AlertDescription>
        </Alert>
        <template v-else-if="flagResult !== null">
          <div class="rounded-md border bg-muted p-3 font-mono text-sm break-all">{{ flagResult }}</div>
        </template>
        <DialogFooter>
          <Button v-if="flagError" variant="outline" :disabled="flagPending" @click="() => accessFlag()">
            {{ $t('重试') }}
          </Button>
          <Button @click="closeFlagAccess">{{ $t('关闭') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
