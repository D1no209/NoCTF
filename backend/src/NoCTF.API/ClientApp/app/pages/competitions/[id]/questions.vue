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
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse,
} from '~/api'

definePageMeta({ middleware: 'auth' })

type Question = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

const route = useRoute()
const competitionId = route.params.id as string

// 列表
const questions = ref<Question[]>([])
const loading = ref(true)
const listError = ref<string | null>(null)

async function loadList() {
  const { data, error } = await listCompetitionQuestions({
    path: { competitionId },
    query: { limit: 100 },
  })
  loading.value = false
  if (error || !data) {
    listError.value = parseApiError(error, '加载咨询列表失败').message
    return
  }
  questions.value = data.items ?? []
}

onMounted(loadList)

// 新建
const createOpen = ref(false)
const createSubject = ref<'Challenge' | 'Platform'>('Challenge')
const createChallengeId = ref<string>('none')
const createTitle = ref('')
const createBody = ref('')
const createPending = ref(false)
const challenges = ref<NoCtfapiEndpointsChallengesChallengeResponse[]>([])

watch(createOpen, async (open) => {
  if (!open || challenges.value.length) return
  const { data } = await listChallengesEndpoint({ path: { competitionId } })
  challenges.value = (data?.items ?? []).filter((c) => c.isPublished)
})

async function submitCreate() {
  if (!createTitle.value.trim() || !createBody.value.trim()) return
  createPending.value = true
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
  createPending.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '提交咨询失败').message)
    return
  }
  toast.success('咨询已提交')
  createOpen.value = false
  createTitle.value = ''
  createBody.value = ''
  await loadList()
  select(data.id!)
}

// 详情
const selectedId = ref<string | null>(null)
const detail = ref<Question | null>(null)
const detailLoading = ref(false)

async function select(id: string) {
  selectedId.value = id
  detailLoading.value = true
  const { data, error } = await getCompetitionQuestion({ path: { competitionId, questionId: id } })
  detailLoading.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '加载咨询详情失败').message)
    return
  }
  detail.value = data
}

// 追加消息
const reply = ref('')
const replyPending = ref(false)

async function submitReply() {
  if (!reply.value.trim() || !detail.value) return
  replyPending.value = true
  const { data, error } = await addCompetitionQuestionMessage({
    path: { competitionId, questionId: detail.value.id! },
    body: { body: reply.value.trim(), expectedRevision: detail.value.revision ?? 0 },
  })
  replyPending.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '发送失败').message)
    return
  }
  detail.value = data
  reply.value = ''
}

// 状态流转(解决 / 关闭)
const statusPending = ref(false)

async function changeStatus(status: 'Resolved' | 'Closed') {
  if (!detail.value) return
  statusPending.value = true
  const { data, error } = await changeCompetitionQuestionStatus({
    path: { competitionId, questionId: detail.value.id! },
    body: { status, expectedRevision: detail.value.revision ?? 0 },
  })
  statusPending.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '状态更新失败').message)
    return
  }
  detail.value = data
  toast.success(status === 'Resolved' ? '咨询已标记为已解决' : '咨询已关闭')
  void loadList()
}

const statusVariant = (status?: string) =>
  status === 'Pending'
    ? ('secondary' as const)
    : status === 'Replied'
      ? ('default' as const)
      : ('outline' as const)
const statusLabel = (status?: string) =>
  ({ Pending: '待回复', Replied: '已回复', Resolved: '已解决', Closed: '已关闭' })[status ?? ''] ?? status
</script>

<template>
  <div class="grid gap-6 lg:grid-cols-5">
    <div class="flex flex-col gap-4 lg:col-span-2">
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">咨询问答</h2>
        <Dialog v-model:open="createOpen">
          <DialogTrigger as-child>
            <Button size="sm">发起咨询</Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>发起咨询</DialogTitle>
              <DialogDescription>向主办方提问,回复后可选择公开给所有选手</DialogDescription>
            </DialogHeader>
            <form @submit.prevent="submitCreate">
              <FieldGroup>
                <Field>
                  <FieldLabel>类型</FieldLabel>
                  <Select v-model="createSubject">
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="Challenge">题目相关</SelectItem>
                        <SelectItem value="Platform">平台/赛事相关</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field v-if="createSubject === 'Challenge'">
                  <FieldLabel>关联题目(可选)</FieldLabel>
                  <Select v-model="createChallengeId">
                    <SelectTrigger><SelectValue placeholder="不关联题目" /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="none">不关联题目</SelectItem>
                        <SelectItem v-for="c in challenges" :key="c.id" :value="c.id!">
                          {{ c.title }}
                        </SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="q-title">标题</FieldLabel>
                  <Input id="q-title" v-model="createTitle" required maxlength="128" />
                </Field>
                <Field>
                  <FieldLabel for="q-body">内容</FieldLabel>
                  <Textarea id="q-body" v-model="createBody" rows="5" required />
                </Field>
                <Field>
                  <Button type="submit" class="w-full" :disabled="createPending">
                    <Spinner v-if="createPending" data-icon="inline-start" />
                    提交
                  </Button>
                </Field>
              </FieldGroup>
            </form>
          </DialogContent>
        </Dialog>
      </div>

      <Alert v-if="listError" variant="destructive">
        <AlertDescription>{{ listError }}</AlertDescription>
      </Alert>
      <Skeleton v-else-if="loading" class="h-40 w-full" />
      <Empty v-else-if="!questions.length" class="border py-8">
        <EmptyHeader>
          <EmptyTitle>暂无咨询</EmptyTitle>
          <EmptyDescription>遇到问题?向主办方发起咨询</EmptyDescription>
        </EmptyHeader>
      </Empty>
      <ul v-else class="flex flex-col gap-2">
        <li v-for="q in questions" :key="q.id">
          <button
            type="button"
            class="w-full rounded-md border px-3 py-2 text-left transition-colors hover:border-primary/50"
            :class="selectedId === q.id ? 'border-primary' : ''"
            @click="select(q.id!)"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="truncate text-sm font-medium">{{ q.title }}</span>
              <Badge :variant="statusVariant(q.status)">{{ statusLabel(q.status) }}</Badge>
            </div>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ q.subject === 'Challenge' ? '题目' : '平台' }} · {{ formatDateTime(q.updatedAt) }}
            </p>
          </button>
        </li>
      </ul>
    </div>

    <div class="lg:col-span-3">
      <Empty v-if="!selectedId" class="border py-16">
        <EmptyHeader>
          <EmptyTitle>选择左侧的咨询查看对话</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Skeleton v-else-if="detailLoading" class="h-64 w-full" />
      <Card v-else-if="detail">
        <CardHeader>
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="text-base">{{ detail.title }}</CardTitle>
            <Badge :variant="statusVariant(detail.status)">{{ statusLabel(detail.status) }}</Badge>
          </div>
          <CardDescription>{{ detail.body }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-4">
          <Separator />
          <ul class="flex flex-col gap-3">
            <li v-for="entry in detail.entries ?? []" :key="entry.id" class="flex flex-col gap-1">
              <template v-if="entry.kind === 'Message'">
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <Badge :variant="entry.actorRole === 'Handler' ? 'default' : 'secondary'">
                    {{ entry.actorRole === 'Handler' ? '主办方' : '我' }}
                  </Badge>
                  <span>{{ entry.actorDisplayName }}</span>
                  <span>{{ formatDateTime(entry.createdAt) }}</span>
                </div>
                <p class="rounded-md bg-muted px-3 py-2 text-sm whitespace-pre-line">{{ entry.body }}</p>
              </template>
              <p v-else-if="entry.kind === 'StatusTransition'" class="text-xs text-muted-foreground">
                状态变更:{{ statusLabel(entry.fromStatus ?? undefined) }} → {{ statusLabel(entry.toStatus ?? undefined) }}
                · {{ formatDateTime(entry.createdAt) }}
              </p>
              <p v-else class="text-xs text-muted-foreground">
                该回复已公开 · {{ formatDateTime(entry.createdAt) }}
              </p>
            </li>
          </ul>

          <template v-if="detail.canReply">
            <Separator />
            <form @submit.prevent="submitReply">
              <FieldGroup>
                <Field>
                  <FieldLabel for="q-reply">追加消息</FieldLabel>
                  <Textarea id="q-reply" v-model="reply" rows="3" required />
                </Field>
                <div class="flex flex-wrap items-center gap-2">
                  <Button type="submit" :disabled="replyPending || !reply.trim()">
                    <Spinner v-if="replyPending" data-icon="inline-start" />
                    发送
                  </Button>
                  <Button
                    v-if="detail.canResolve"
                    type="button"
                    variant="outline"
                    :disabled="statusPending"
                    @click="changeStatus('Resolved')"
                  >
                    标记已解决
                  </Button>
                  <Button
                    v-if="detail.canClose"
                    type="button"
                    variant="outline"
                    :disabled="statusPending"
                    @click="changeStatus('Closed')"
                  >
                    关闭咨询
                  </Button>
                </div>
              </FieldGroup>
            </form>
          </template>
          <p v-else class="text-sm text-muted-foreground">该咨询已{{ statusLabel(detail.status) }},无法继续回复。</p>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
