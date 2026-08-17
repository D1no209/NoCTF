<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  getGameplayFactStatusEndpoint,
  submitFixEndpoint,
  uploadPatchEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '~/api'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  /** 例如 RequireBreakBeforeFix 且尚未 Break 成功时禁用 */
  disabled?: boolean
  disabledReason?: string
}>()

const emit = defineEmits<{ accepted: [], evaluated: [] }>()

const file = ref<File | null>(null)
const pending = ref(false)
const stage = ref<'idle' | 'uploading' | 'submitting' | 'evaluating'>('idle')
type EvaluationResult = Pick<NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse, 'state' | 'result'>
const result = ref<EvaluationResult | null>(null)

function onFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  file.value = target.files?.[0] ?? null
}

const { start: startPolling, stop: stopPolling } = usePolling(
  async () => {
    if (!gameplayFactId.value) return true
    const { data, error } = await getGameplayFactStatusEndpoint({
      path: { competitionId: props.competitionId, gameplayFactId: gameplayFactId.value },
    })
    if (error || !data) return false
    result.value = { state: data.state, result: data.result }
    if (!isGameplayFactPending(data.state)) {
      if (data.result === 'Correct') toast.success(translate("Fix 评测完成:修复生效"))
      else toast.error(translate('Fix 评测完成：{result}', { result: gameplayFactResultLabel(data.result) }))
      emit('evaluated')
      return true
    }
    return false
  },
  { interval: 2000, timeout: 300_000 },
)

const gameplayFactId = ref<string | null>(null)

async function submit() {
  if (!file.value || props.disabled) return
  pending.value = true
  result.value = null
  gameplayFactId.value = null
  try {
    stage.value = 'uploading'
    const { data: upload, error: uploadError } = await uploadPatchEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: { file: file.value },
    })
    if (uploadError || !upload?.patchUploadId) {
      toast.error(parseApiError(uploadError, translate("补丁上传失败")).message)
      return
    }
    stage.value = 'submitting'
    const { data: accepted, error: submitError } = await submitFixEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: { patchUploadId: upload.patchUploadId },
    })
    if (submitError || !accepted?.gameplayFactId) {
      toast.error(parseApiError(submitError, translate("Fix 提交失败")).message)
      return
    }
    gameplayFactId.value = accepted.gameplayFactId
    result.value = { state: 'Pending', result: null }
    stage.value = 'evaluating'
    toast.success(translate("Fix 已受理,等待评测"))
    emit('accepted')
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
    gameplayFactStateChanged: (payload) => {
      const id = competitionHubString(payload, 'gameplayFactId')
      if (id && id === gameplayFactId.value) {
        void getGameplayFactStatusEndpoint({
          path: { competitionId: props.competitionId, gameplayFactId: id },
        }).then(({ data }) => {
          if (!data) return
          result.value = { state: data.state, result: data.result }
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
      <CardTitle class="text-base">{{ $t('提交 Fix(修复归档)') }}</CardTitle>
      <CardDescription> {{ $t('上传包含修复内容的 tar.gz 归档,平台将重建靶机并验证漏洞是否修复') }} </CardDescription>
    </CardHeader>
    <CardContent>
      <Alert v-if="disabled" class="mb-4">
        <AlertDescription>{{ disabledReason ?? $t('当前不可提交 Fix。') }}</AlertDescription>
      </Alert>
      <form @submit.prevent="submit">
        <FieldGroup>
          <Field>
            <FieldLabel :for="`patch-file-${competitionChallengeId}`">{{ $t('修复归档(.tar.gz)') }}</FieldLabel>
            <Input
              :id="`patch-file-${competitionChallengeId}`"
              type="file"
              accept=".tar.gz,.tgz,application/gzip"
              :disabled="disabled"
              @change="onFileChange"
            />
            <FieldDescription v-if="file">{{ $t('已选择：{file}（{size}）', { file: file.name, size: formatBytes(file.size) }) }}</FieldDescription>
          </Field>
          <Field>
            <Button type="submit" :disabled="pending || !file || disabled">
              <Spinner v-if="pending" data-icon="inline-start" />
              {{ stage === 'uploading' ? $t('上传中…') : stage === 'submitting' ? $t('提交中…') : $t('上传并提交 Fix') }}
            </Button>
          </Field>
        </FieldGroup>
      </form>

      <Alert v-if="result" class="mt-4" :variant="isGameplayFactPending(result.state) ? 'default' : result.result === 'Correct' ? 'default' : 'destructive'">
        <AlertDescription class="flex items-center gap-2">
          <Spinner v-if="isGameplayFactPending(result.state)" class="size-3" />
          <span v-if="isGameplayFactPending(result.state)">{{ gameplayFactStateLabel(result.state) }}…</span>
          <span v-else>{{ $t('评测结果:') }}<strong>{{ gameplayFactResultLabel(result.result) }}</strong></span>
        </AlertDescription>
      </Alert>
    </CardContent>
  </Card>
</template>
