<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  getSubmissionStatusEndpoint,
  submitFixEndpoint,
  uploadPatchEndpoint,
} from '~/api'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  /** 例如 RequireBreakBeforeFix 且尚未 Break 成功时禁用 */
  disabled?: boolean
  disabledReason?: string
}>()

const emit = defineEmits<{ evaluated: [] }>()

const file = ref<File | null>(null)
const pending = ref(false)
const stage = ref<'idle' | 'uploading' | 'submitting' | 'evaluating'>('idle')
const result = ref<{ state?: number, result?: number | null } | null>(null)

function onFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  file.value = target.files?.[0] ?? null
}

const { start: startPolling, stop: stopPolling } = usePolling(
  async () => {
    if (!submissionId.value) return true
    const { data, error } = await getSubmissionStatusEndpoint({
      path: { competitionId: props.competitionId, submissionId: submissionId.value },
    })
    if (error || !data) return false
    result.value = { state: data.evaluationState, result: data.result }
    if (!isEvaluationPending(data.evaluationState)) {
      if (data.result === ScoringResult.Correct) toast.success('Fix 评测完成:修复生效')
      else toast.error(`Fix 评测完成:${scoringResultLabel(data.result)}`)
      emit('evaluated')
      return true
    }
    return false
  },
  { interval: 2000, timeout: 300_000 },
)

const submissionId = ref<string | null>(null)

async function submit() {
  if (!file.value || props.disabled) return
  pending.value = true
  result.value = null
  submissionId.value = null
  try {
    stage.value = 'uploading'
    const { data: upload, error: uploadError } = await uploadPatchEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: { file: file.value },
    })
    if (uploadError || !upload?.patchUploadId) {
      toast.error(parseApiError(uploadError, '补丁上传失败').message)
      return
    }
    stage.value = 'submitting'
    const { data: accepted, error: submitError } = await submitFixEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: { patchUploadId: upload.patchUploadId },
    })
    if (submitError || !accepted?.submissionId) {
      toast.error(parseApiError(submitError, 'Fix 提交失败').message)
      return
    }
    submissionId.value = accepted.submissionId
    result.value = { state: EvaluationState.Pending, result: null }
    stage.value = 'evaluating'
    toast.success('Fix 已受理,等待评测')
    startPolling()
  }
  finally {
    pending.value = false
  }
}

// 实时:提交结果推送 → 立即刷新
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    submissionResult: (payload) => {
      const id = (payload as { submissionId?: unknown })?.submissionId
      if (typeof id === 'string' && id === submissionId.value) {
        void getSubmissionStatusEndpoint({
          path: { competitionId: props.competitionId, submissionId: id },
        }).then(({ data }) => {
          if (!data) return
          result.value = { state: data.evaluationState, result: data.result }
        })
      }
    },
  })
})
onUnmounted(() => {
  unwatch?.()
  stopPolling()
})
</script>

<template>
  <Card>
    <CardHeader>
      <CardTitle class="text-base">提交 Fix(修复归档)</CardTitle>
      <CardDescription>
        上传包含修复内容的 tar.gz 归档,平台将重建靶机并验证漏洞是否修复
      </CardDescription>
    </CardHeader>
    <CardContent>
      <Alert v-if="disabled" class="mb-4">
        <AlertDescription>{{ disabledReason ?? '当前不可提交 Fix。' }}</AlertDescription>
      </Alert>
      <form @submit.prevent="submit">
        <FieldGroup>
          <Field>
            <FieldLabel :for="`patch-file-${competitionChallengeId}`">修复归档(.tar.gz)</FieldLabel>
            <Input
              :id="`patch-file-${competitionChallengeId}`"
              type="file"
              accept=".tar.gz,.tgz,application/gzip"
              :disabled="disabled"
              @change="onFileChange"
            />
            <FieldDescription v-if="file">已选择:{{ file.name }}({{ formatBytes(file.size) }})</FieldDescription>
          </Field>
          <Field>
            <Button type="submit" :disabled="pending || !file || disabled">
              <Spinner v-if="pending" data-icon="inline-start" />
              {{ stage === 'uploading' ? '上传中…' : stage === 'submitting' ? '提交中…' : '上传并提交 Fix' }}
            </Button>
          </Field>
        </FieldGroup>
      </form>

      <Alert v-if="result" class="mt-4" :variant="isEvaluationPending(result.state) ? 'default' : result.result === ScoringResult.Correct ? 'default' : 'destructive'">
        <AlertDescription class="flex items-center gap-2">
          <Spinner v-if="isEvaluationPending(result.state)" class="size-3" />
          <span v-if="isEvaluationPending(result.state)">{{ evaluationStateLabel(result.state) }}…</span>
          <span v-else>评测结果:<strong>{{ scoringResultLabel(result.result) }}</strong></span>
        </AlertDescription>
      </Alert>
    </CardContent>
  </Card>
</template>
