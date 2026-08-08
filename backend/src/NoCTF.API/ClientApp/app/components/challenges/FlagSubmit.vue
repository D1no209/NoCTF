<script setup lang="ts">
import { toast } from 'vue-sonner'
import { getSubmissionStatusEndpoint, submitFlagEndpoint } from '~/api'

interface TrackedSubmission {
  id: string
  state?: string
  result?: string | null
  failureCode?: string | null
}

const props = withDefaults(
  defineProps<{
    competitionId: string
    competitionChallengeId: string
    /** AWD 批量提交:多行输入,一次提交多个 flag */
    multiple?: boolean
    title?: string
    description?: string
  }>(),
  { multiple: false, title: '提交 Flag', description: '' },
)

const emit = defineEmits<{ evaluated: [] }>()

const input = ref('')
const submitting = ref(false)
const tracked = ref<TrackedSubmission[]>([])
const toasted = new Set<string>()

const { polling, timedOut, start: startPolling, stop: stopPolling } = usePolling(
  async () => {
    await refreshPending()
    return !tracked.value.some((t) => isEvaluationPending(t.state))
  },
  { interval: 1500, timeout: 120_000 },
)

async function refreshOne(id: string): Promise<void> {
  const { data, error } = await getSubmissionStatusEndpoint({
    path: { competitionId: props.competitionId, submissionId: id },
  })
  if (error || !data) return
  const item = tracked.value.find((t) => t.id === id)
  const wasPending = item ? isEvaluationPending(item.state) : true
  if (item) {
    item.state = data.evaluationState
    item.result = data.result
    item.failureCode = data.failureCode
  }
  else {
    tracked.value.unshift({
      id,
      state: data.evaluationState,
      result: data.result,
      failureCode: data.failureCode,
    })
  }
  if (wasPending && !isEvaluationPending(data.evaluationState) && !toasted.has(id)) {
    toasted.add(id)
    if (data.result === ScoringResult.Correct) toast.success('提交评测完成:正确')
    else toast.error(`提交评测完成:${scoringResultLabel(data.result)}`)
    emit('evaluated')
  }
}

async function refreshPending(): Promise<void> {
  const pending = tracked.value.filter((t) => isEvaluationPending(t.state))
  await Promise.all(pending.map((t) => refreshOne(t.id)))
}

async function submit() {
  const lines = props.multiple
    ? input.value.split('\n').map((line) => line.trim()).filter(Boolean)
    : [input.value.trim()].filter(Boolean)
  if (!lines.length) return
  submitting.value = true
  const { data, error } = await submitFlagEndpoint({
    path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
    body: props.multiple ? { flags: lines } : { flag: lines[0] },
  })
  submitting.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '提交失败').message)
    return
  }
  const ids = [
    ...(data.submissionId ? [data.submissionId] : []),
    ...(data.submissions ?? []).map((s) => s.submissionId).filter((id): id is string => !!id),
  ]
  for (const id of ids) {
    if (!tracked.value.some((t) => t.id === id)) {
      tracked.value.unshift({ id, state: EvaluationState.Pending, result: null })
    }
  }
  input.value = ''
  toast.success(`已受理 ${ids.length} 条提交,评测中`)
  startPolling()
}

// 实时:服务端推送提交结果 → 立即刷新该提交
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    submissionResult: (payload) => {
      const id = (payload as { submissionId?: unknown })?.submissionId
      if (typeof id === 'string' && tracked.value.some((t) => t.id === id)) {
        void refreshOne(id)
      }
    },
  })
})
onUnmounted(() => {
  unwatch?.()
  stopPolling()
})

function resultVariant(result?: string | null) {
  if (result === ScoringResult.Correct) return 'default' as const
  return 'destructive' as const
}
</script>

<template>
  <Card>
    <CardHeader>
      <CardTitle class="text-base">{{ title }}</CardTitle>
      <CardDescription v-if="description">{{ description }}</CardDescription>
    </CardHeader>
    <CardContent>
      <form @submit.prevent="submit">
        <FieldGroup>
          <Field>
            <FieldLabel :for="`flag-input-${competitionChallengeId}`">
              {{ multiple ? 'Flag 列表(每行一个)' : 'Flag' }}
            </FieldLabel>
            <Textarea
              v-if="multiple"
              :id="`flag-input-${competitionChallengeId}`"
              v-model="input"
              rows="4"
              class="font-mono"
              placeholder="flag{...}"
            />
            <Input
              v-else
              :id="`flag-input-${competitionChallengeId}`"
              v-model="input"
              class="font-mono"
              placeholder="flag{...}"
            />
          </Field>
          <Field>
            <Button type="submit" :disabled="submitting || !input.trim()">
              <Spinner v-if="submitting" data-icon="inline-start" />
              提交
            </Button>
          </Field>
        </FieldGroup>
      </form>

      <div v-if="tracked.length" class="mt-4 flex flex-col gap-2">
        <Alert v-if="timedOut">
          <AlertDescription>评测结果等待超时,可稍后在「我的提交」查看结果。</AlertDescription>
        </Alert>
        <Alert
          v-for="item in tracked"
          :key="item.id"
          :variant="isEvaluationPending(item.state) ? 'default' : resultVariant(item.result)"
        >
          <AlertDescription class="flex items-center gap-2">
            <Spinner v-if="isEvaluationPending(item.state)" class="size-3" />
            <span v-if="isEvaluationPending(item.state)">
              {{ evaluationStateLabel(item.state) }}…
            </span>
            <span v-else>
              评测结果:<strong>{{ scoringResultLabel(item.result) }}</strong>
            </span>
          </AlertDescription>
        </Alert>
      </div>
    </CardContent>
  </Card>
</template>
