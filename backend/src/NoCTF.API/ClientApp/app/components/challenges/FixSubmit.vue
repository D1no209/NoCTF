<script setup lang="ts">
import { toast } from 'vue-sonner'
import { requestAwdpDefenseTargetEndpoint, uploadPatchEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse,
  NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol,
  NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol,
} from '~/api'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  defense?: NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse
}>()

const emit = defineEmits<{ changed: [], accepted: [] }>()

const file = ref<File | null>(null)
const pendingAction = ref<'request' | 'upload' | null>(null)

const requestFailureLabels: Record<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, string> = {
  DefenseNotAvailable: '当前比赛、队伍或题目不允许申请防御验证环境。',
  ActiveDefenseTargetExists: '已有一次性防御验证环境，请先完成或等待其回收。',
  BreakRequired: '本题要求先完成一次有效 Break，才能申请防御验证。',
  FixAttemptsExhausted: '本题的 Fix 尝试次数已用尽。',
  InvalidRuntimeConfiguration: '一次性防御验证环境配置无效，请联系比赛工作人员。',
  DefenseTargetConcurrency: '防御验证环境状态已变化，请刷新后重试。',
}

const uploadFailureLabels: Record<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol, string> = {
  ArchiveStreamNotSeekable: '无法验证该 Fix 归档，请重新选择文件。',
  ArchiveInvalid: 'Fix 归档格式无效，请上传有效的 tar.gz 文件。',
  DefenseTargetNotReady: '该一次性防御验证环境尚未就绪或已被回收。',
  FixAttemptsExhausted: '本题的 Fix 尝试次数已用尽。',
  DefenseTargetConsumed: '该一次性防御验证环境已绑定过 Fix，不能再次上传。',
}

const runtimeActive = computed(() => {
  const state = props.defense?.runtimeState
  return state === 'Queued' || state === 'Provisioning' || state === 'Running' || state === 'Stopping'
})
const targetCreating = computed(() => {
  const state = props.defense?.runtimeState
  return state === 'Queued' || state === 'Provisioning'
})
const canUpload = computed(() =>
  props.defense?.runtimeState === 'Running'
  && props.defense.stage === 'AwaitingPatch'
  && !props.defense.gameplayFactId,
)
const validating = computed(() =>
  props.defense?.state === 'Pending'
  || props.defense?.state === 'Queued'
  || props.defense?.state === 'Processing'
  || props.defense?.stage === 'PatchApplying'
  || props.defense?.stage === 'CheckerRunning',
)
const recycling = computed(() => props.defense?.runtimeState === 'Stopping')
const targetFailed = computed(() => props.defense?.runtimeState === 'Failed')
const completedAndRecycled = computed(() =>
  props.defense?.runtimeState === 'Stopped' && !!props.defense.gameplayFactId,
)
const canRequest = computed(() => !runtimeActive.value)

function protocolCode<T extends string>(error: unknown): T | null {
  if (!error || typeof error !== 'object' || !('code' in error)) return null
  const code = (error as { code?: unknown }).code
  return typeof code === 'string' ? code as T : null
}

function onFileChange(event: Event): void {
  const target = event.target as HTMLInputElement
  file.value = target.files?.[0] ?? null
}

async function requestTarget(): Promise<void> {
  if (!canRequest.value || pendingAction.value) return
  pendingAction.value = 'request'
  try {
    const { data, error } = await requestAwdpDefenseTargetEndpoint({
      path: {
        competitionId: props.competitionId,
        competitionChallengeId: props.competitionChallengeId,
      },
    })
    if (error || !data?.runtimeInstanceId) {
      const code = protocolCode<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol>(error)
      toast.error(code ? translate(requestFailureLabels[code]) : parseApiError(error, translate('申请防御验证环境失败')).message)
      return
    }
    toast.success(translate('已申请一次性防御验证环境，正在创建干净 target。'))
    emit('changed')
  }
  finally {
    pendingAction.value = null
  }
}

async function uploadFix(): Promise<void> {
  const runtimeInstanceId = props.defense?.runtimeInstanceId
  if (!file.value || !canUpload.value || !runtimeInstanceId || pendingAction.value) return
  pendingAction.value = 'upload'
  try {
    const { data, error } = await uploadPatchEndpoint({
      path: {
        competitionId: props.competitionId,
        competitionChallengeId: props.competitionChallengeId,
        runtimeInstanceId,
      },
      body: { file: file.value },
    })
    if (error || !data?.gameplayFactId) {
      const code = protocolCode<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol>(error)
      const message = code
        ? uploadFailureLabels[code]
        : parseApiError(error, translate('Fix 上传失败')).message
      toast.error(translate(message))
      return
    }
    file.value = null
    toast.success(translate('Fix 已锁定到本次验证环境，正在执行一次性 Checker。'))
    emit('accepted')
    emit('changed')
  }
  finally {
    pendingAction.value = null
  }
}
</script>

<template>
  <Card>
    <CardHeader>
      <CardTitle class="text-base">{{ $t('一次性防御验证') }}</CardTitle>
      <CardDescription>
        {{ $t('每次申请都会创建一个全新的干净 target；上传一次 Fix 后立即锁定，只执行一次 Checker，完成后自动回收全部资源。') }}
      </CardDescription>
    </CardHeader>
    <CardContent class="flex flex-col gap-4">
      <Alert v-if="targetCreating">
        <Spinner class="mr-2 inline size-3" />
        <AlertDescription class="inline">{{ $t('正在创建一次性防御验证环境…') }}</AlertDescription>
      </Alert>

      <form v-else-if="canUpload" class="flex flex-col gap-4" @submit.prevent="uploadFix">
        <Alert>
          <AlertDescription>
            {{ $t('干净 target 已就绪。该环境只接受一个 Fix 归档，上传后不可替换或再次上传。') }}
          </AlertDescription>
        </Alert>
        <FieldGroup>
          <Field>
            <FieldLabel :for="`patch-file-${competitionChallengeId}`">{{ $t('本次 Fix 归档（.tar.gz）') }}</FieldLabel>
            <Input
              :id="`patch-file-${competitionChallengeId}`"
              type="file"
              accept=".tar.gz,.tgz,application/gzip"
              :disabled="pendingAction !== null"
              @change="onFileChange"
            />
            <FieldDescription v-if="file">
              {{ $t('已选择：{file}（{size}）', { file: file.name, size: formatBytes(file.size) }) }}
            </FieldDescription>
          </Field>
          <Field>
            <Button type="submit" :disabled="pendingAction !== null || !file">
              <Spinner v-if="pendingAction === 'upload'" data-icon="inline-start" />
              {{ pendingAction === 'upload' ? $t('上传并锁定中…') : $t('上传本次 Fix 包') }}
            </Button>
          </Field>
        </FieldGroup>
      </form>

      <Alert v-else-if="validating">
        <Spinner class="mr-2 inline size-3" />
        <AlertDescription class="inline">
          {{ $t('Fix 已锁定，正在应用补丁并执行一次性验证 Checker。') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="recycling">
        <Spinner class="mr-2 inline size-3" />
        <AlertDescription class="inline">
          {{ $t('验证已经结束，正在回收 target、Checker、网络、端口和容量。') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="completedAndRecycled">
        <AlertDescription>
          {{ $t('本次 Fix 验证已完成，一次性防御验证环境已回收。再次尝试必须申请新的干净环境。') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="targetFailed" variant="destructive">
        <AlertDescription>
          {{ $t('一次性防御验证环境创建失败或已失效，可以重新申请。') }}
        </AlertDescription>
      </Alert>

      <Button v-if="canRequest" :disabled="pendingAction !== null" @click="requestTarget">
        <Spinner v-if="pendingAction === 'request'" data-icon="inline-start" />
        {{ pendingAction === 'request' ? $t('申请中…') : completedAndRecycled || targetFailed ? $t('重新申请防御') : $t('申请防御环境') }}
      </Button>
    </CardContent>
  </Card>
</template>
