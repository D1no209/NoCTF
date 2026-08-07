<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAccessCompetitionSubmissionFlag,
  adminGetSubmission,
  adminListCompetitionChallenges,
  adminListSubmissions,
  adminListTeams,
  adminQueueSubmissionEvaluation,
  adminRejudgeSubmission,
  adminRejudgeSubmissions,
} from '~/api'
import type {
  NoCtfapiEndpointsSubmissionsAdminSubmissionStatusResponse,
  NoCtfapiEndpointsSubmissionsSubmissionListItemResponse,
  NoCtfDomainSubmissionsScoringResult,
  NoCtfDomainSubmissionsSubmissionEvaluationState,
  NoCtfDomainSubmissionsSubmissionKind,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

// ---- Reference data for filters ----
const challengeOptions = ref<{ id: string; title: string }[]>([])
const teamOptions = ref<{ id: string; name: string }[]>([])
const teamName = (id?: string | null) => teamOptions.value.find(t => t.id === id)?.name ?? id ?? '—'
const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '—'

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
const filterKind = ref('')
const filterState = ref('')
const filterResult = ref('')
const filterFlag = ref('')

const { items, loading, hasMore, loadMore, reset, initialized } = useCursorPagination<
  NoCtfapiEndpointsSubmissionsSubmissionListItemResponse
>(async (cursor) => {
  const { data, error } = await adminListSubmissions({
    path: { competitionId },
    query: {
      competitionChallengeId: filterChallenge.value || null,
      teamId: filterTeam.value || null,
      submissionKind: filterKind.value === '' ? null : Number(filterKind.value) as NoCtfDomainSubmissionsSubmissionKind,
      evaluationState: filterState.value === '' ? null : Number(filterState.value) as NoCtfDomainSubmissionsSubmissionEvaluationState,
      scoringResult: filterResult.value === '' ? null : Number(filterResult.value) as NoCtfDomainSubmissionsScoringResult,
      submittedFlag: filterFlag.value || null,
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

// ---- Detail sheet ----
const detail = ref<NoCtfapiEndpointsSubmissionsAdminSubmissionStatusResponse | null>(null)
const detailOpen = ref(false)
const detailLoading = ref(false)

async function openDetail(id?: string) {
  if (!id) return
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  const { data, error } = await adminGetSubmission({ path: { competitionId, submissionId: id } })
  if (error) toast.error(parseApiError(error).message)
  else detail.value = data ?? null
  detailLoading.value = false
}

// ---- Rejudge / queue evaluation ----
const actionPending = ref<string | null>(null)

async function rejudgeOne(submissionId?: string) {
  if (!submissionId) return
  actionPending.value = submissionId
  try {
    const { error } = await adminRejudgeSubmission({ path: { competitionId, submissionId } })
    if (error) throw error
    toast.success('已加入重判队列')
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
    const { error } = await adminRejudgeSubmissions({
      path: { competitionId },
      body: { competitionChallengeId: batchTarget.value },
    })
    if (error) throw error
    toast.success('已将题目全部提交加入重判队列')
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
    const { error } = await adminQueueSubmissionEvaluation({
      path: { competitionId },
      body: { competitionChallengeId: batchTarget.value },
    })
    if (error) throw error
    toast.success('已触发评测队列')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    actionPending.value = null
  }
}

// ---- Protected flag access (audited) ----
const flagDialog = ref<{ submissionId: string } | null>(null)
const flagReason = ref('')
const flagResult = ref<string | null>(null)
const flagPending = ref(false)

function openFlagAccess(submissionId?: string) {
  if (!submissionId) return
  flagDialog.value = { submissionId }
  flagReason.value = ''
  flagResult.value = null
}

async function accessFlag() {
  const ctx = flagDialog.value
  if (!ctx || !flagReason.value.trim()) return
  flagPending.value = true
  try {
    const { data, error } = await adminAccessCompetitionSubmissionFlag({
      path: { competitionId, submissionId: ctx.submissionId },
      body: { reason: flagReason.value.trim() },
    })
    if (error) throw error
    flagResult.value = data?.submittedFlag ?? '(无内容)'
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
              <SelectValue placeholder="题目(全部)" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterTeam">
            <SelectTrigger class="w-full">
              <SelectValue placeholder="队伍(全部)" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterKind">
            <SelectTrigger class="w-full">
              <SelectValue placeholder="类型(全部)" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="0">Flag</SelectItem>
                <SelectItem value="1">Break</SelectItem>
                <SelectItem value="2">Fix</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterState">
            <SelectTrigger class="w-full">
              <SelectValue placeholder="评测状态(全部)" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="0">待处理</SelectItem>
                <SelectItem value="1">排队中</SelectItem>
                <SelectItem value="2">评测中</SelectItem>
                <SelectItem value="3">已完成</SelectItem>
                <SelectItem value="4">平台失败</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterResult">
            <SelectTrigger class="w-full">
              <SelectValue placeholder="结果(全部)" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="0">正确</SelectItem>
                <SelectItem value="1">错误</SelectItem>
                <SelectItem value="2">重复</SelectItem>
                <SelectItem value="3">次数耗尽</SelectItem>
                <SelectItem value="4">平台失败</SelectItem>
                <SelectItem value="5">已拒绝</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Input v-model="filterFlag" placeholder="提交的 Flag 包含…" />
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <Button size="sm" @click="applyFilters">应用筛选</Button>
          <Button
            variant="ghost"
            size="sm"
            @click="filterChallenge = ''; filterTeam = ''; filterKind = ''; filterState = ''; filterResult = ''; filterFlag = ''; applyFilters()"
          >
            清空
          </Button>
          <template v-if="canWrite">
            <Separator orientation="vertical" class="h-6" />
            <Select v-model="batchTarget">
              <SelectTrigger class="w-48">
                <SelectValue placeholder="选择题目(批量操作)" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="rejudgeBatch">
              <Spinner v-if="actionPending === 'batch'" data-icon="inline-start" />
              整题重判
            </Button>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="queueEvaluation">
              <Spinner v-if="actionPending === 'queue'" data-icon="inline-start" />
              触发评测
            </Button>
          </template>
        </div>
      </CardContent>
    </Card>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && items.length === 0">
      <EmptyHeader>
        <EmptyTitle>没有符合条件的提交</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>队伍</TableHead>
            <TableHead>题目</TableHead>
            <TableHead class="w-20">类型</TableHead>
            <TableHead class="w-24">评测状态</TableHead>
            <TableHead class="w-24">结果</TableHead>
            <TableHead class="w-44">提交时间</TableHead>
            <TableHead class="w-52 text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="s in items" :key="s.id">
            <TableCell class="font-medium">{{ teamName(s.teamId) }}</TableCell>
            <TableCell>{{ challengeTitle(s.competitionChallengeId) }}</TableCell>
            <TableCell>{{ enumLabel(SubmissionKindLabel, s.kind) }}</TableCell>
            <TableCell>
              <Badge :variant="s.evaluationState === 3 ? 'default' : s.evaluationState === 4 ? 'destructive' : 'secondary'">
                {{ enumLabel(SubmissionEvaluationStateLabel, s.evaluationState) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="s.result !== null && s.result !== undefined" :variant="s.result === 0 ? 'default' : 'outline'">
                {{ enumLabel(ScoringResultLabel, s.result) }}
              </Badge>
              <span v-else class="text-muted-foreground">—</span>
            </TableCell>
            <TableCell>{{ adminFormatDateTime(s.receivedAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(s.id)">详情</Button>
                <Button variant="ghost" size="sm" @click="openFlagAccess(s.id)">读取 Flag</Button>
                <Button v-if="canWrite" variant="ghost" size="sm" :disabled="actionPending === s.id" @click="rejudgeOne(s.id)">
                  <Spinner v-if="actionPending === s.id" data-icon="inline-start" />
                  重判
                </Button>
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
          <SheetTitle>提交详情</SheetTitle>
          <SheetDescription>提交 ID:{{ detail?.submissionId }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">队伍</span><span>{{ teamName(detail.teamId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">题目</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">提交人</span><span class="font-mono text-xs">{{ detail.submittedByUserId }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">类型</span><span>{{ enumLabel(SubmissionKindLabel, detail.kind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">评测状态</span><span>{{ enumLabel(SubmissionEvaluationStateLabel, detail.evaluationState) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">结果</span><span>{{ detail.result !== null && detail.result !== undefined ? enumLabel(ScoringResultLabel, detail.result) : '—' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">失败代码</span><span>{{ detail.failureCode ?? '—' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">提交时间</span><span>{{ adminFormatDateTime(detail.receivedAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">评测更新时间</span><span>{{ adminFormatDateTime(detail.evaluationUpdatedAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">处理版本</span><span>{{ detail.processingVersion }}</span></div>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="flagDialog !== null" @update:open="(v) => { if (!v) flagDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>读取受保护的提交 Flag</DialogTitle>
          <DialogDescription>该操作会被记录审计日志,请填写读取原因。</DialogDescription>
        </DialogHeader>
        <template v-if="flagResult === null">
          <FieldGroup>
            <Field>
              <FieldLabel for="flag-reason">读取原因</FieldLabel>
              <Textarea id="flag-reason" v-model="flagReason" required />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="flagDialog = null">取消</Button>
            <Button :disabled="flagPending || !flagReason.trim()" @click="accessFlag">
              <Spinner v-if="flagPending" data-icon="inline-start" />
              读取
            </Button>
          </DialogFooter>
        </template>
        <template v-else>
          <div class="rounded-md border bg-muted p-3 font-mono text-sm break-all">{{ flagResult }}</div>
          <DialogFooter>
            <Button @click="flagDialog = null">关闭</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
  </div>
</template>
