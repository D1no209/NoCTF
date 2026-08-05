<script setup lang="ts">
import type {
  CompetitionQuestion,
  CompetitionQuestionStatus,
  CompetitionQuestionSubject,
} from '@/api/questionApi'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  AlertTriangle,
  CheckCircle2,
  Eye,
  Loader2,
  LockKeyhole,
  MessageSquare,
  Plus,
  RefreshCw,
  Send,
  X,
} from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { queryKeys } from '@/api/queryKeys'
import { questionApi } from '@/api/questionApi'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Textarea } from '@/components/ui/textarea'

const props = withDefaults(defineProps<{
  competitionId: string
  subject: CompetitionQuestionSubject
  competitionChallengeId?: string | null
  canCreate?: boolean
  compact?: boolean
}>(), {
  competitionChallengeId: null,
  canCreate: false,
  compact: false,
})

const { t, locale } = useI18n()
const queryClient = useQueryClient()
const selectedQuestionId = ref<string | null>(null)
const composing = ref(false)
const title = ref('')
const body = ref('')
const replyBody = ref('')
const creating = ref(false)
const replying = ref(false)
const changingStatus = ref(false)
const publishingEntryId = ref<string | null>(null)

const listKey = computed(() => queryKeys.competitionQuestions(
  props.competitionId,
  props.subject,
  props.competitionChallengeId,
))

const {
  data: questions,
  isLoading,
  isError,
  refetch,
} = useQuery<CompetitionQuestion[]>({
  queryKey: listKey,
  queryFn: () => questionApi.list(props.competitionId, {
    subject: props.subject,
    competitionChallengeId: props.competitionChallengeId,
  }),
  enabled: computed(() => Boolean(props.competitionId)),
})

const detailKey = computed(() => queryKeys.competitionQuestion(
  props.competitionId,
  selectedQuestionId.value ?? '',
))
const {
  data: selectedQuestion,
  isLoading: detailLoading,
} = useQuery<CompetitionQuestion>({
  queryKey: detailKey,
  queryFn: () => questionApi.get(props.competitionId, selectedQuestionId.value!),
  enabled: computed(() => Boolean(selectedQuestionId.value)),
})

const visibleQuestions = computed(() => questions.value ?? [])
const messageEntries = computed(() => selectedQuestion.value?.entries?.filter(entry =>
  entry.kind === 'Message' || entry.kind === 'StatusTransition',
) ?? [])
const canSubmitQuestion = computed(() => title.value.trim().length >= 4
  && body.value.trim().length >= 4
  && !creating.value)
const canReply = computed(() => Boolean(
  selectedQuestion.value?.canReply
  && replyBody.value.trim().length >= 4
  && !replying.value,
))

watch(() => [props.competitionId, props.competitionChallengeId, props.subject], () => {
  selectedQuestionId.value = null
  composing.value = false
  title.value = ''
  body.value = ''
  replyBody.value = ''
})

function questionId(question: CompetitionQuestion) {
  return question.id ?? ''
}

function statusLabel(status?: CompetitionQuestionStatus) {
  return status ? t(`questions.status.${status}`) : t('common.unknown')
}

function statusVariant(status?: CompetitionQuestionStatus) {
  switch (status) {
    case 'Pending': return 'destructive' as const
    case 'Replied': return 'default' as const
    case 'Resolved': return 'outline' as const
    case 'Closed': return 'secondary' as const
    default: return 'outline' as const
  }
}

function formatDate(value?: string | null) {
  if (!value)
    return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat(locale.value, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(parsed)
}

async function invalidateQuestions(question?: CompetitionQuestion) {
  if (question?.id) {
    queryClient.setQueryData(
      queryKeys.competitionQuestion(props.competitionId, question.id),
      question,
    )
  }
  await queryClient.invalidateQueries({ queryKey: listKey.value })
}

async function createQuestion() {
  if (!canSubmitQuestion.value)
    return
  creating.value = true
  try {
    const question = await questionApi.create(props.competitionId, {
      subject: props.subject,
      competitionChallengeId: props.subject === 'Challenge'
        ? props.competitionChallengeId
        : null,
      submissionId: null,
      title: title.value.trim(),
      body: body.value.trim(),
    })
    title.value = ''
    body.value = ''
    composing.value = false
    selectedQuestionId.value = question.id ?? null
    await invalidateQuestions(question)
    toast.success(t('questions.created'))
  }
  catch {
    toast.error(t('questions.createFailed'))
  }
  finally {
    creating.value = false
  }
}

async function addReply() {
  const question = selectedQuestion.value
  if (!question?.id || question.revision === undefined || !canReply.value)
    return
  replying.value = true
  try {
    const updated = await questionApi.addMessage(props.competitionId, question.id, {
      body: replyBody.value.trim(),
      expectedRevision: question.revision,
    })
    replyBody.value = ''
    await invalidateQuestions(updated)
    toast.success(t('questions.replySent'))
  }
  catch {
    toast.error(t('questions.operationFailed'))
    await queryClient.invalidateQueries({ queryKey: detailKey.value })
  }
  finally {
    replying.value = false
  }
}

async function changeStatus(status: CompetitionQuestionStatus) {
  const question = selectedQuestion.value
  if (!question?.id || question.revision === undefined || changingStatus.value)
    return
  changingStatus.value = true
  try {
    const updated = await questionApi.changeStatus(props.competitionId, question.id, {
      status,
      expectedRevision: question.revision,
    })
    await invalidateQuestions(updated)
    toast.success(t('questions.statusUpdated'))
  }
  catch {
    toast.error(t('questions.operationFailed'))
    await queryClient.invalidateQueries({ queryKey: detailKey.value })
  }
  finally {
    changingStatus.value = false
  }
}

async function publishReply(entryId?: string) {
  const question = selectedQuestion.value
  if (!question?.id || !entryId || question.revision === undefined || publishingEntryId.value)
    return
  publishingEntryId.value = entryId
  try {
    const updated = await questionApi.publish(props.competitionId, question.id, {
      replyEntryId: entryId,
      expectedRevision: question.revision,
    })
    await invalidateQuestions(updated)
    toast.success(t('questions.published'))
  }
  catch {
    toast.error(t('questions.operationFailed'))
    await queryClient.invalidateQueries({ queryKey: detailKey.value })
  }
  finally {
    publishingEntryId.value = null
  }
}
</script>

<template>
  <Card class="p-1">
    <Panel :class="compact ? 'gap-3 p-3' : 'gap-4 p-4'">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div class="flex min-w-0 items-start gap-3">
          <div class="border bg-background p-2 text-primary">
            <MessageSquare class="size-4" />
          </div>
          <div class="min-w-0">
            <div class="flex flex-wrap items-center gap-2">
              <h3 class="text-sm font-black uppercase tracking-wide">
                {{ subject === 'Challenge' ? t('questions.challengeTitle') : t('questions.platformTitle') }}
              </h3>
              <Badge variant="outline">
                <LockKeyhole class="mr-1 size-3" />
                {{ t('questions.privateByDefault') }}
              </Badge>
            </div>
            <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
              {{ t('questions.description') }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <Button type="button" variant="ghost" size="icon-sm" :title="t('common.refresh')" @click="refetch()">
            <RefreshCw class="size-4" />
          </Button>
          <Button
            v-if="canCreate"
            type="button"
            size="sm"
            variant="outline"
            @click="composing = !composing"
          >
            <X v-if="composing" class="size-4" />
            <Plus v-else class="size-4" />
            {{ composing ? t('common.cancel') : t('questions.newQuestion') }}
          </Button>
        </div>
      </div>

      <Alert variant="warning" class="text-xs">
        <AlertTriangle class="size-4" />
        {{ t('questions.sensitiveWarning') }}
      </Alert>

      <form v-if="composing" class="grid gap-3 border bg-background/70 p-3" @submit.prevent="createQuestion">
        <div class="grid gap-1.5">
          <Label for="question-title">{{ t('questions.subjectLabel') }}</Label>
          <Input
            id="question-title"
            v-model="title"
            maxlength="160"
            :placeholder="t('questions.subjectPlaceholder')"
          />
        </div>
        <div class="grid gap-1.5">
          <Label for="question-body">{{ t('questions.bodyLabel') }}</Label>
          <Textarea
            id="question-body"
            v-model="body"
            rows="5"
            maxlength="4000"
            :placeholder="t('questions.bodyPlaceholder')"
          />
          <div class="text-right font-mono text-[10px] text-muted-foreground">
            {{ body.length }}/4000
          </div>
        </div>
        <div class="flex justify-end">
          <Button type="submit" size="sm" :disabled="!canSubmitQuestion">
            <Loader2 v-if="creating" class="size-4 animate-spin" />
            <Send v-else class="size-4" />
            {{ t('questions.submit') }}
          </Button>
        </div>
      </form>

      <div v-if="isLoading" class="flex items-center gap-2 py-6 text-sm text-muted-foreground">
        <Loader2 class="size-4 animate-spin" />
        {{ t('questions.loading') }}
      </div>
      <Alert v-else-if="isError" variant="destructive">
        {{ t('questions.loadFailed') }}
      </Alert>
      <div v-else-if="!visibleQuestions.length" class="border border-dashed px-4 py-8 text-center">
        <MessageSquare class="mx-auto size-6 text-muted-foreground" />
        <p class="mt-2 text-sm font-semibold">
          {{ t('questions.empty') }}
        </p>
        <p class="mt-1 text-xs text-muted-foreground">
          {{ t('questions.emptyHint') }}
        </p>
      </div>
      <div v-else class="grid gap-2">
        <button
          v-for="question in visibleQuestions"
          :key="questionId(question)"
          type="button"
          class="group flex w-full items-start justify-between gap-3 border bg-background/70 px-3 py-3 text-left transition-colors hover:border-primary/60"
          :class="selectedQuestionId === question.id ? 'border-primary bg-primary/5' : ''"
          @click="selectedQuestionId = question.id ?? null"
        >
          <div class="min-w-0">
            <div class="truncate text-sm font-semibold">
              {{ question.title }}
            </div>
            <div class="mt-1 flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
              <span>{{ question.access === 'Public' ? t('questions.anonymousParticipant') : question.askerDisplayName }}</span>
              <span>·</span>
              <span>{{ formatDate(question.updatedAt) }}</span>
              <Badge v-if="question.publishedAt" variant="outline" class="h-5 px-1.5 text-[9px]">
                <Eye class="mr-1 size-3" />
                {{ t('questions.publishedBadge') }}
              </Badge>
            </div>
          </div>
          <Badge :variant="statusVariant(question.status)">
            {{ statusLabel(question.status) }}
          </Badge>
        </button>
      </div>

      <div v-if="selectedQuestionId" class="border-t pt-4">
        <div v-if="detailLoading" class="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <Loader2 class="size-4 animate-spin" />
          {{ t('questions.loadingDetail') }}
        </div>
        <div v-else-if="selectedQuestion" class="grid gap-4">
          <div class="border bg-muted/20 p-3">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div class="text-sm font-black">
                  {{ selectedQuestion.title }}
                </div>
                <div class="mt-1 text-[11px] text-muted-foreground">
                  {{ selectedQuestion.access === 'Public' ? t('questions.anonymousParticipant') : selectedQuestion.askerDisplayName }}
                  <span v-if="selectedQuestion.teamDisplayName"> · {{ selectedQuestion.teamDisplayName }}</span>
                </div>
              </div>
              <Badge :variant="statusVariant(selectedQuestion.status)">
                {{ statusLabel(selectedQuestion.status) }}
              </Badge>
            </div>
            <p class="mt-3 whitespace-pre-wrap text-sm leading-relaxed">
              {{ selectedQuestion.body }}
            </p>
          </div>

          <div class="grid gap-2">
            <div
              v-for="entry in messageEntries"
              :key="entry.id"
              class="border px-3 py-2.5"
              :class="entry.kind === 'StatusTransition' ? 'border-dashed bg-muted/20' : entry.actorRole === 'Handler' ? 'border-primary/30 bg-primary/5' : 'bg-background'"
            >
              <template v-if="entry.kind === 'Message'">
                <div class="flex flex-wrap items-center justify-between gap-2 text-[11px] text-muted-foreground">
                  <span class="font-semibold text-foreground">{{ entry.actorDisplayName }}</span>
                  <span>{{ formatDate(entry.createdAt) }}</span>
                </div>
                <p class="mt-2 whitespace-pre-wrap text-sm leading-relaxed">
                  {{ entry.body }}
                </p>
                <div v-if="entry.actorRole === 'Handler' && selectedQuestion.canPublish && !entry.publishedAt" class="mt-3 flex justify-end">
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    :disabled="Boolean(publishingEntryId)"
                    :title="t('questions.publishHint')"
                    @click="publishReply(entry.id)"
                  >
                    <Loader2 v-if="publishingEntryId === entry.id" class="size-4 animate-spin" />
                    <Eye v-else class="size-4" />
                    {{ t('questions.publishReviewed') }}
                  </Button>
                </div>
                <Badge v-if="entry.publishedAt" variant="outline" class="mt-2">
                  <Eye class="mr-1 size-3" />
                  {{ t('questions.publishedBadge') }}
                </Badge>
              </template>
              <div v-else class="flex items-center gap-2 text-xs text-muted-foreground">
                <CheckCircle2 class="size-3.5" />
                {{ t('questions.statusTransition', {
                  from: statusLabel(entry.fromStatus ?? undefined),
                  to: statusLabel(entry.toStatus ?? undefined),
                }) }}
              </div>
            </div>
          </div>

          <form v-if="selectedQuestion.canReply" class="grid gap-2" @submit.prevent="addReply">
            <Textarea
              v-model="replyBody"
              rows="4"
              maxlength="4000"
              :placeholder="t('questions.replyPlaceholder')"
            />
            <div class="flex items-center justify-between gap-3">
              <span class="font-mono text-[10px] text-muted-foreground">{{ replyBody.length }}/4000</span>
              <Button type="submit" size="sm" :disabled="!canReply">
                <Loader2 v-if="replying" class="size-4 animate-spin" />
                <Send v-else class="size-4" />
                {{ t('questions.sendReply') }}
              </Button>
            </div>
          </form>

          <div v-if="selectedQuestion.canResolve || selectedQuestion.canClose" class="flex flex-wrap justify-end gap-2 border-t pt-3">
            <Button
              v-if="selectedQuestion.canResolve"
              type="button"
              size="sm"
              variant="outline"
              :disabled="changingStatus"
              @click="changeStatus('Resolved')"
            >
              <CheckCircle2 class="size-4" />
              {{ t('questions.resolve') }}
            </Button>
            <Button
              v-if="selectedQuestion.canClose"
              type="button"
              size="sm"
              variant="destructive"
              :disabled="changingStatus"
              @click="changeStatus('Closed')"
            >
              <X class="size-4" />
              {{ t('questions.closePermanently') }}
            </Button>
          </div>
        </div>
      </div>
    </Panel>
  </Card>
</template>
