<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  addCompetitionQuestionMessage,
  changeCompetitionQuestionStatus,
  createCompetitionQuestion,
  getCompetitionQuestion,
  listChallengesEndpoint,
  listCompetitionQuestions,
} from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionSubjectCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse,
} from '~/api'
import { createLatestRequestGuard } from '~/lib/latest-request'
import {
  competitionQuestionErrorMessage,
  competitionQuestionRoleLabel,
  isCompetitionQuestionHandlerRole,
  mergeCompetitionQuestions,
} from '~/lib/competition-question'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'
import {
  maximumQuestionBodyLength,
  maximumQuestionTitleLength,
  minimumQuestionBodyLength,
  minimumQuestionTitleLength,
  validateCompetitionQuestionDraft,
} from '~/lib/participant-form-validation'

definePageMeta({ middleware: 'auth' })

type Question = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

const route = useRoute()
const router = useRouter()
const competitionId = route.params.id as string
const { markRead, unreadCount } = useCompetitionQuestionReadState(competitionId)

// 列表
const refreshError = ref<string | null>(null)

async function fetchQuestionPage(cursor: string | null) {
  const { data, error } = await listCompetitionQuestions({
    path: { competitionId },
    query: { cursor, limit: 50 },
  })
  if (error || !data)
    throw parseApiError(error, translate("加载咨询列表失败"))
  return { items: data.items ?? [], nextCursor: data.nextCursor ?? null }
}

const {
  items: questions,
  loading,
  error: paginationError,
  hasMore,
  initialized,
  loadMore,
} = useCursorPagination<Question>(fetchQuestionPage)

const listError = computed(() => refreshError.value ?? paginationError.value?.message ?? null)

async function loadMoreQuestions() {
  refreshError.value = null
  await loadMore()
  questions.value = mergeCompetitionQuestions([], questions.value)
}

const refreshList = createTrailingRefresh(async () => {
  if (!initialized.value) {
    await loadMoreQuestions()
    return
  }

  refreshError.value = null
  try {
    const page = await fetchQuestionPage(null)
    questions.value = mergeCompetitionQuestions(questions.value, page.items ?? [])
    paginationError.value = null
  }
  catch (error) {
    refreshError.value = parseApiError(error, translate("加载咨询列表失败")).message
  }
})

function upsertQuestion(question: Question) {
  questions.value = mergeCompetitionQuestions(questions.value, [question])
}

// 新建
const createOpen = ref(false)
const createSubject = ref<NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionSubjectCode>('Challenge')
const createChallengeId = ref<string>('none')
const createTitle = ref('')
const createBody = ref('')
const createPending = ref(false)
const createError = ref<string | null>(null)
const challenges = ref<NoCtfapiEndpointsChallengesChallengeResponse[]>([])

watch(createOpen, async (open) => {
  if (!open || challenges.value.length) return
  const { data } = await listChallengesEndpoint({ path: { competitionId } })
  challenges.value = (data?.items ?? []).filter((c) => c.isPublished)
})

async function submitCreate() {
  if (createPending.value) return
  createError.value = validateCompetitionQuestionDraft({
    requiresChallenge: createSubject.value === 'Challenge',
    challengeId: createChallengeId.value === 'none' ? null : createChallengeId.value,
    title: createTitle.value,
    body: createBody.value,
  })
  if (createError.value) return

  createPending.value = true
  try {
    const { data, error } = await createCompetitionQuestion({
      path: { competitionId },
      body: {
        subject: createSubject.value,
        competitionChallengeId:
          createSubject.value === 'Challenge' && createChallengeId.value !== 'none'
            ? createChallengeId.value
            : null,
        title: createTitle.value.trim(),
        body: createBody.value.trim(),
      },
    })
    if (error || !data) {
      createError.value = competitionQuestionErrorMessage(error, translate("提交咨询失败"))
      toast.error(createError.value)
      return
    }
    toast.success(translate("咨询已提交"))
    createOpen.value = false
    createTitle.value = ''
    createBody.value = ''
    createError.value = null
    upsertQuestion(data)
    await select(data.id!)
  }
  catch (error) {
    createError.value = competitionQuestionErrorMessage(error, translate("提交咨询失败"))
    toast.error(createError.value)
  }
  finally {
    createPending.value = false
  }
}

function setCreateOpen(open: boolean) {
  if (createPending.value) return
  createOpen.value = open
  if (!open) createError.value = null
}

// 详情
const selectedId = ref<string | null>(null)
const detail = ref<Question | null>(null)
const detailLoading = ref(false)
const detailRequests = createLatestRequestGuard()

function applyDetailQuestion(question: Question, markAsRead = true) {
  const currentDetail = detail.value
  const current = currentDetail && currentDetail.id === question.id ? [currentDetail] : []
  const fresh = mergeCompetitionQuestions(current, [question])[0] ?? question
  detail.value = fresh
  upsertQuestion(fresh)
  if (markAsRead) markRead(fresh)
}

async function select(id: string, syncRoute = true) {
  if (detailLoading.value && selectedId.value === id) return
  const request = detailRequests.begin()
  selectedId.value = id
  detailLoading.value = true
  if (syncRoute && route.query.question !== id) {
    void router.replace({ query: { ...route.query, question: id } })
  }
  try {
    const { data, error } = await getCompetitionQuestion({ path: { competitionId, questionId: id } })
    if (!detailRequests.isCurrent(request) || selectedId.value !== id) return
    if (error || !data) {
      toast.error(parseApiError(error, translate("加载咨询详情失败")).message)
      return
    }
    applyDetailQuestion(data)
  }
  catch (error) {
    if (detailRequests.isCurrent(request))
      toast.error(parseApiError(error, translate("加载咨询详情失败")).message)
  }
  finally {
    if (detailRequests.isCurrent(request))
      detailLoading.value = false
  }
}

watch(() => route.query.question, async (value) => {
  const questionId = typeof value === 'string' ? value : null
  if (questionId && questionId !== selectedId.value)
    await select(questionId, false)
})

const refreshSelectedDetail = createTrailingRefresh(async () => {
  const questionId = selectedId.value
  if (!questionId) return

  try {
    const { data, error } = await getCompetitionQuestion({ path: { competitionId, questionId } })
    if (error || !data || selectedId.value !== questionId) return
    applyDetailQuestion(data, document.visibilityState === 'visible')
  }
  catch {
    // Background refresh failures keep the currently displayed thread intact.
  }
})

async function refreshFromServer() {
  await Promise.all([refreshList(), refreshSelectedDetail()])
}

let unwatchCompetition: (() => void) | undefined
let disposed = false
onMounted(async () => {
  const questionId = typeof route.query.question === 'string' ? route.query.question : null
  await Promise.all([
    loadMoreQuestions(),
    questionId ? select(questionId, false) : Promise.resolve(),
  ])
  if (disposed) return
  unwatchCompetition = watchCompetition(competitionId, {
    competitionEventChanged: () => void refreshFromServer(),
    onReconnected: () => void refreshFromServer(),
  })
})
onUnmounted(() => {
  disposed = true
  detailRequests.invalidate()
  unwatchCompetition?.()
})

// 追加消息
const reply = ref('')
const replyPending = ref(false)
const replyError = ref<string | null>(null)

async function submitReply() {
  if (replyPending.value || !reply.value.trim() || !detail.value?.canReply) return
  replyError.value = null
  replyPending.value = true
  try {
    const { data, error } = await addCompetitionQuestionMessage({
      path: { competitionId, questionId: detail.value.id! },
      body: { body: reply.value.trim(), expectedRevision: detail.value.revision ?? 0 },
    })
    if (error || !data) {
      replyError.value = competitionQuestionErrorMessage(error, translate("发送失败"))
      toast.error(replyError.value)
      return
    }
    applyDetailQuestion(data)
    reply.value = ''
    toast.success(translate("消息已发送"))
  }
  catch (error) {
    replyError.value = competitionQuestionErrorMessage(error, translate("发送失败"))
    toast.error(replyError.value)
  }
  finally {
    replyPending.value = false
  }
}

// 状态流转(解决 / 关闭)
const statusPending = ref(false)

async function changeStatus(status: 'Resolved' | 'Closed') {
  if (!detail.value || statusPending.value) return
  statusPending.value = true
  try {
    const { data, error } = await changeCompetitionQuestionStatus({
      path: { competitionId, questionId: detail.value.id! },
      body: { status, expectedRevision: detail.value.revision ?? 0 },
    })
    if (error || !data) {
      toast.error(competitionQuestionErrorMessage(error, translate("状态更新失败")))
      return
    }
    applyDetailQuestion(data)
    toast.success(status === 'Resolved' ? translate("咨询已标记为已解决") : translate("咨询已关闭"))
  }
  catch (error) {
    toast.error(competitionQuestionErrorMessage(error, translate("状态更新失败")))
  }
  finally {
    statusPending.value = false
  }
}

const statusVariant = (status?: string) =>
  status === 'Pending'
    ? ('secondary' as const)
    : status === 'Replied'
      ? ('default' as const)
      : ('outline' as const)
const statusLabel = (status?: string) =>
  ({ Pending: translate("待回复"), Replied: translate("已回复"), Resolved: translate("已解决"), Closed: translate("已关闭") })[status ?? ''] ?? status
const roleLabel = (role?: NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode) =>
  role ? translate(competitionQuestionRoleLabel[role]) : translate("未知角色")
const isHandlerRole = (role?: NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode) =>
  isCompetitionQuestionHandlerRole(role)
const participantLimitReached = computed(() =>
  detail.value?.access === 'Asker'
  && detail.value.status !== 'Closed'
  && (detail.value.participantMessagesRemaining ?? 0) <= 0,
)
</script>

<template>
  <div class="grid gap-6 lg:grid-cols-5">
    <div class="flex flex-col gap-4 lg:col-span-2">
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ $t('咨询问答') }}</h2>
        <Dialog :open="createOpen" @update:open="setCreateOpen">
          <DialogTrigger as-child>
            <Button size="sm">{{ $t('发起咨询') }}</Button>
          </DialogTrigger>
          <DialogContent class="max-h-[calc(100vh-2rem)] overflow-y-auto sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ $t('发起咨询') }}</DialogTitle>
              <DialogDescription>{{ $t('咨询内容默认仅本队与有权工作人员可见；需要公开的信息将通过 Hint 或比赛公告发布。') }}</DialogDescription>
            </DialogHeader>
            <form @submit.prevent>
              <FieldGroup>
                <Field>
                  <FieldLabel>{{ $t('类型') }}</FieldLabel>
                  <Select v-model="createSubject">
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="Challenge">{{ $t('题目相关') }}</SelectItem>
                        <SelectItem value="Platform">{{ $t('平台/赛事相关') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field v-if="createSubject === 'Challenge'">
                  <FieldLabel>{{ $t('关联题目(必选)') }}</FieldLabel>
                  <Select v-model="createChallengeId">
                    <SelectTrigger><SelectValue :placeholder="$t('不关联题目')" /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="none">{{ $t('不关联题目') }}</SelectItem>
                        <SelectItem v-for="c in challenges" :key="c.id" :value="c.id!">
                          {{ c.title }}
                        </SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="q-title">{{ $t('标题') }}</FieldLabel>
                  <Input
                    id="q-title"
                    v-model="createTitle"
                    required
                    :minlength="minimumQuestionTitleLength"
                    :maxlength="maximumQuestionTitleLength"
                    @input="createError = null"
                  />
                </Field>
                <Field>
                  <FieldLabel for="q-body">{{ $t('内容') }}</FieldLabel>
                  <Textarea
                    id="q-body"
                    v-model="createBody"
                    class="min-h-44"
                    rows="10"
                    required
                    :minlength="minimumQuestionBodyLength"
                    :maxlength="maximumQuestionBodyLength"
                    @input="createError = null"
                  />
                </Field>
                <p v-if="createError" role="alert" class="text-sm text-destructive">
                  {{ createError }}
                </p>
                <Field>
                  <Button type="button" class="w-full" :disabled="createPending" @click="submitCreate">
                    <Spinner v-if="createPending" data-icon="inline-start" /> {{ $t('提交') }} </Button>
                </Field>
              </FieldGroup>
            </form>
          </DialogContent>
        </Dialog>
      </div>

      <Alert v-if="listError" variant="destructive">
        <AlertDescription>{{ listError }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading && !initialized" class="h-40 w-full" />
      <Empty v-else-if="initialized && !questions.length" class="border py-8">
        <EmptyHeader>
          <EmptyTitle>{{ $t('暂无咨询') }}</EmptyTitle>
          <EmptyDescription>{{ $t('遇到问题?向主办方发起咨询') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
      <ul v-else-if="questions.length" class="flex flex-col gap-2">
        <li v-for="q in questions" :key="q.id">
          <button
            type="button"
            class="w-full rounded-md border px-3 py-2 text-left transition-colors hover:border-primary/50"
            :class="selectedId === q.id ? 'border-primary' : ''"
            @click="select(q.id!)"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="min-w-0 truncate text-sm font-medium">{{ q.title }}</span>
              <div class="flex shrink-0 items-center gap-1.5">
                <Badge v-if="unreadCount(q) > 0" variant="destructive">
                  {{ $t('{count} 条未读', { count: unreadCount(q) }) }}
                </Badge>
                <Badge :variant="statusVariant(q.status)">{{ statusLabel(q.status) }}</Badge>
              </div>
            </div>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ q.subject === 'Challenge' ? $t('题目 · {title}', { title: q.challengeTitle ?? $t('未知题目') }) : $t('平台 / 赛事') }}
            </p>
            <p class="mt-1 truncate text-xs text-muted-foreground">
              {{ $t('最近由 {role} {actor} 更新 · {time}', { role: roleLabel(q.lastActorRole), actor: q.lastActorDisplayName ?? '-', time: formatDateTime(q.updatedAt) }) }}
            </p>
          </button>
        </li>
      </ul>
      <div v-if="!initialized && listError" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMoreQuestions">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('重新加载') }} </Button>
      </div>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMoreQuestions">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('加载更多') }} </Button>
      </div>
    </div>

    <div class="lg:col-span-3">
      <Empty v-if="!selectedId" class="border py-16">
        <EmptyHeader>
          <EmptyTitle>{{ $t('选择左侧的咨询查看对话') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Skeleton v-else-if="detailLoading" class="h-64 w-full" />
      <Card v-else-if="detail">
        <CardHeader>
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="text-base">{{ detail.title }}</CardTitle>
            <Badge :variant="statusVariant(detail.status)">{{ statusLabel(detail.status) }}</Badge>
          </div>
          <CardDescription>
            {{ detail.subject === 'Challenge' ? $t('题目咨询 · {title}', { title: detail.challengeTitle ?? $t('未知题目') }) : $t('平台 / 赛事咨询') }}
            · {{ detail.teamDisplayName ?? detail.askerDisplayName }}
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-4">
          <Separator />
          <ul class="flex flex-col gap-3">
            <li v-for="entry in detail.entries ?? []" :key="entry.id" class="flex flex-col gap-1">
              <template v-if="entry.kind === 'Message'">
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <Badge :variant="isHandlerRole(entry.actorRole) ? 'default' : 'secondary'">
                    {{ roleLabel(entry.actorRole) }}
                  </Badge>
                  <span>{{ entry.actorDisplayName }}</span>
                  <span>{{ formatDateTime(entry.createdAt) }}</span>
                </div>
                <p
                  class="rounded-md border px-3 py-2 text-sm whitespace-pre-line"
                  :class="isHandlerRole(entry.actorRole) ? 'border-primary/20 bg-primary/5' : 'border-border bg-muted'"
                >
                  {{ entry.body }}
                </p>
              </template>
              <p v-else-if="entry.kind === 'StatusTransition'" class="text-xs text-muted-foreground">
                {{ $t('状态变更：{from} → {to}', { from: statusLabel(entry.fromStatus ?? undefined) ?? '-', to: statusLabel(entry.toStatus ?? undefined) ?? '-' }) }}
                · {{ roleLabel(entry.actorRole) }} {{ entry.actorDisplayName }}
                · {{ formatDateTime(entry.createdAt) }}
              </p>
            </li>
          </ul>

          <template v-if="detail.canReply">
            <Separator />
            <form @submit.prevent="submitReply">
              <FieldGroup>
                <Field>
                  <FieldLabel for="q-reply">{{ $t('追加消息') }}</FieldLabel>
                  <Textarea id="q-reply" v-model="reply" rows="5" required @input="replyError = null" />
                  <FieldDescription v-if="detail.access === 'Asker'">
                    {{ $t('本轮还可连续发送 {remaining} / {maximum} 条；工作人员回复后重置。', { remaining: detail.participantMessagesRemaining ?? 0, maximum: detail.maxParticipantMessagesBeforeHandlerReply ?? 3 }) }}
                    <span v-if="detail.status === 'Resolved'">{{ $t('继续追问会将咨询重新设为待处理。') }}</span>
                  </FieldDescription>
                  <FieldDescription v-else>{{ $t('工作人员回复不受连续消息额度限制。') }}</FieldDescription>
                </Field>
                <p v-if="replyError" role="alert" class="text-sm text-destructive">{{ replyError }}</p>
                <div class="flex flex-wrap items-center gap-2">
                  <Button type="submit" :disabled="replyPending || !reply.trim()">
                    <Spinner v-if="replyPending" data-icon="inline-start" /> {{ $t('发送') }} </Button>
                  <Button
                    v-if="detail.canResolve"
                    type="button"
                    variant="outline"
                    :disabled="statusPending"
                    @click="changeStatus('Resolved')"
                  > {{ $t('标记已解决') }} </Button>
                  <Button
                    v-if="detail.canClose"
                    type="button"
                    variant="outline"
                    :disabled="statusPending"
                    @click="changeStatus('Closed')"
                  > {{ $t('关闭咨询') }} </Button>
                </div>
              </FieldGroup>
            </form>
          </template>
          <Alert v-else-if="participantLimitReached">
            <AlertDescription>
              {{ $t('工作人员回复前最多连续发送 {maximum} 条消息，请等待回复；历史内容仍会完整保留。', { maximum: detail.maxParticipantMessagesBeforeHandlerReply ?? 3 }) }}
            </AlertDescription>
          </Alert>
          <p v-else-if="detail.access === 'Observer'" class="text-sm text-muted-foreground">{{ $t('你对该咨询只有只读权限。') }}</p>
          <p v-else class="text-sm text-muted-foreground">{{ $t('该咨询已{status}，无法继续回复。', { status: statusLabel(detail.status) ?? '-' }) }}</p>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
