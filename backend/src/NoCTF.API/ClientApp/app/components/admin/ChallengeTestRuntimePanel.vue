<script setup lang="ts">
import { Check, Clipboard, FlaskConical, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { publicGatewayFailure } from '~/utils/public-gateway'
import {
  adminChallengeBankExtendTestRuntime,
  adminChallengeBankGetTestRuntime,
  adminChallengeBankResetTestRuntime,
  adminChallengeBankStartTestRuntime,
  adminChallengeBankStopTestRuntime,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse,
  NoCtfapiEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol,
} from '~/api'
import type {
  ChallengeTestRuntimeLoadOutcome,
  ChallengeTestRuntimeMutationKind,
  PendingChallengeTestRuntimeMutation,
} from '~/utils/challenge-test-runtime-polling'

type TestRuntime = NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse

const props = defineProps<{
  challengeId: string
  definitionDirty: boolean
}>()

const runtime = ref<TestRuntime | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const acting = ref(false)
const pendingMutation = ref<PendingChallengeTestRuntimeMutation | null>(null)
const copied = ref(false)
const now = ref(Date.now())
const extendMinutes = ref(30)
let copiedTimer: ReturnType<typeof setTimeout> | undefined
let clockTimer: ReturnType<typeof setInterval> | undefined

async function load(): Promise<ChallengeTestRuntimeLoadOutcome> {
  try {
    const { data, error, response } = await adminChallengeBankGetTestRuntime({
      path: { challengeId: props.challengeId },
    })
    if (response?.status === 404) {
      runtime.value = null
      loadError.value = null
      return 'missing'
    }
    if (error || !data) {
      loadError.value = parseApiError(error, translate('加载题目测试容器失败')).message
      return 'failed'
    }
    runtime.value = data
    loadError.value = null
    return 'available'
  }
  catch (error) {
    loadError.value = parseApiError(error, translate('加载题目测试容器失败')).message
    return 'failed'
  }
}

function shouldContinuePolling(outcome: ChallengeTestRuntimeLoadOutcome): boolean {
  const decision = evaluateChallengeTestRuntimePolling(
    outcome,
    runtime.value,
    pendingMutation.value,
  )
  if (decision.mutationObserved)
    pendingMutation.value = null
  return decision.continuePolling
}

const { timedOut, start: startPolling } = usePolling(
  async () => {
    const outcome = await load()
    return !shouldContinuePolling(outcome)
  },
  { interval: 1500, timeout: 180_000 },
)

onMounted(async () => {
  const outcome = await load()
  loading.value = false
  if (shouldContinuePolling(outcome))
    startPolling()
  clockTimer = setInterval(() => { now.value = Date.now() }, 1000)
})

onUnmounted(() => {
  if (copiedTimer) clearTimeout(copiedTimer)
  if (clockTimer) clearInterval(clockTimer)
})

async function retryLoad(): Promise<void> {
  loading.value = true
  const outcome = await load()
  loading.value = false
  if (shouldContinuePolling(outcome))
    startPolling()
}

async function act(
  kind: ChallengeTestRuntimeMutationKind,
  action: () => Promise<{
    data?: NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse
    error?: unknown
  }>,
  failureMessage: string,
): Promise<void> {
  if (acting.value) return
  acting.value = true
  try {
    const previousRuntimeInstanceId = runtime.value?.id
    const previousExpiresAt = runtime.value?.expiresAt
    const { data, error } = await action()
    if (error) throw error
    pendingMutation.value = {
      kind,
      runtimeInstanceId: data?.runtimeInstanceId,
      previousRuntimeInstanceId,
      previousExpiresAt,
    }
    toast.success(translate('操作已受理,测试容器状态更新中'))
    const outcome = await load()
    if (shouldContinuePolling(outcome))
      startPolling()
  }
  catch (error) {
    toast.error(parseApiError(error, failureMessage).message)
  }
  finally {
    acting.value = false
  }
}

const path = computed(() => ({ challengeId: props.challengeId }))
const start = () => act(
  'start',
  () => adminChallengeBankStartTestRuntime({ path: path.value }),
  translate('启动题目测试容器失败'),
)
const stop = () => act(
  'stop',
  () => adminChallengeBankStopTestRuntime({ path: path.value }),
  translate('停止题目测试容器失败'),
)
const reset = () => act(
  'reset',
  () => adminChallengeBankResetTestRuntime({ path: path.value }),
  translate('重置题目测试容器失败'),
)
const extend = () => act(
  'extend',
  () => adminChallengeBankExtendTestRuntime({
    path: path.value,
    body: { seconds: Math.max(60, Math.round(extendMinutes.value * 60)) },
  }),
  translate('续期题目测试容器失败'),
)

async function copyTestFlag(): Promise<void> {
  if (!runtime.value?.testFlag) return
  try {
    await navigator.clipboard.writeText(runtime.value.testFlag)
    copied.value = true
    if (copiedTimer) clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => { copied.value = false }, 1800)
  }
  catch {
    toast.error(translate('复制测试 Flag 失败，请手动选择复制'))
  }
}

const active = computed(() => runtime.value?.state === 'Queued'
  || runtime.value?.state === 'Provisioning'
  || runtime.value?.state === 'Running')
const canStart = computed(() => !runtime.value
  || runtime.value.state === 'Stopped'
  || runtime.value.state === 'Failed')
const waitingForAcceptedRuntime = computed(() => {
  const pending = pendingMutation.value
  if (!pending || pending.kind !== 'start' && pending.kind !== 'reset') return false
  if (!runtime.value) return true
  if (pending.runtimeInstanceId)
    return runtime.value.id !== pending.runtimeInstanceId
  return pending.kind === 'reset'
    && runtime.value.id === pending.previousRuntimeInstanceId
})
const busy = computed(() => acting.value || waitingForAcceptedRuntime.value)
const ttl = computed(() => {
  if (!runtime.value?.expiresAt) return null
  const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
  return remaining > 0 ? formatDuration(remaining) : translate('已到期')
})
const canExtend = computed(() => {
  if (runtime.value?.state !== 'Running' || !runtime.value.expiresAt) return false
  const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
  return remaining > 0 && remaining < 10 * 60_000
})
const stateVariant = computed(() => {
  if (runtime.value?.state === 'Running') return 'default' as const
  if (runtime.value?.state === 'Failed') return 'destructive' as const
  if (runtime.value?.state === 'Stopped') return 'secondary' as const
  return 'outline' as const
})
const flagVariant = computed(() => runtime.value?.flagState === 'Failed'
  ? 'destructive' as const
  : runtime.value?.flagState === 'Succeeded'
    ? 'default' as const
    : 'secondary' as const)

function flagStateLabel(state?: NoCtfapiEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol): string {
  switch (state) {
    case 'Pending': return translate('注入中')
    case 'Succeeded': return translate('已注入')
    case 'Failed': return translate('注入失败')
    case 'Canceled': return translate('已取消')
    default: return translate('无需注入')
  }
}
</script>

<template>
  <section class="mt-6 flex flex-col gap-4 border-t pt-6" aria-labelledby="challenge-test-runtime-title">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex items-start gap-3">
        <span class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <FlaskConical class="size-4" aria-hidden="true" />
        </span>
        <div>
          <h3 id="challenge-test-runtime-title" class="text-base font-semibold">{{ $t('题目测试容器') }}</h3>
          <p class="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {{ $t('使用最近一次保存的题目定义启动真实容器，验证访问入口与动态 Flag 注入。') }}
          </p>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <Badge v-if="runtime" :variant="stateVariant">{{ runtimeStateLabel(runtime.state) }}</Badge>
        <Button type="button" variant="outline" size="sm" :disabled="loading || busy" @click="retryLoad">
          <RefreshCw data-icon="inline-start" />{{ $t('刷新状态') }}
        </Button>
      </div>
    </header>

    <Alert v-if="definitionDirty">
      <AlertDescription>{{ $t('运行环境定义尚未保存。请先保存修改，再启动或重置测试容器。') }}</AlertDescription>
    </Alert>
    <p class="text-xs leading-5 text-muted-foreground">
      {{ $t('停止测试实例只清理容器与网络，不会主动删除 Runner 节点的镜像缓存。') }}
    </p>
    <Skeleton v-if="loading" class="h-28 w-full" />
    <Alert v-else-if="loadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ loadError }}</span>
        <Button type="button" variant="outline" size="sm" @click="retryLoad">{{ $t('重试') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="timedOut" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('测试容器状态更新超时，请手动刷新。') }}</span>
        <Button type="button" variant="outline" size="sm" @click="retryLoad">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>

    <template v-if="!loading && !loadError">
      <Alert v-if="runtime?.publicAccessFailure"><AlertDescription>{{ publicGatewayFailure(runtime.publicAccessFailure) }}</AlertDescription></Alert>
      <div v-if="runtime" class="grid gap-3 text-sm sm:grid-cols-2">
        <div class="grid gap-1">
          <span class="text-muted-foreground">{{ $t('运行位置') }}</span>
          <span>{{ enumLabel(RuntimeKindLabel, runtime.runtimeKind) }} · {{ enumLabel(RuntimeProviderLabel, runtime.provider) }}</span>
        </div>
        <div class="grid gap-1">
          <span class="text-muted-foreground">{{ $t('动态 Flag') }}</span>
          <Badge :variant="flagVariant" class="w-fit">
            <Spinner v-if="runtime.flagState === 'Pending'" class="size-3" />
            {{ flagStateLabel(runtime.flagState) }}
          </Badge>
        </div>
        <div v-if="runtime.state === 'Failed' && runtime.failureCode" class="grid gap-1 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('失败原因') }}</span>
          <span class="text-destructive">{{ enumLabel(RuntimeFailureCodeLabel, runtime.failureCode) }}</span>
        </div>
        <div v-if="runtime.state === 'Running' && runtime.urls?.length" class="grid gap-2 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('访问地址') }}</span>
          <RuntimeAccessUrl v-for="url in runtime.urls" :key="url" :url="url" />
        </div>
        <div v-if="runtime.testFlag" class="grid gap-2 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('本次测试 Flag') }}</span>
          <div class="flex min-w-0 items-center gap-2">
            <code class="min-w-0 flex-1 overflow-x-auto border bg-muted px-3 py-2 font-mono text-xs">{{ runtime.testFlag }}</code>
            <Button type="button" variant="outline" size="sm" @click="copyTestFlag">
              <Check v-if="copied" data-icon="inline-start" />
              <Clipboard v-else data-icon="inline-start" />
              {{ copied ? $t('已复制') : $t('复制') }}
            </Button>
          </div>
        </div>
        <div v-if="runtime.state === 'Running' && ttl" class="sm:col-span-2">
          {{ $t('剩余时间:') }}<span class="font-mono font-medium tabular-nums">{{ ttl }}</span>
        </div>
      </div>
      <p v-else class="text-sm text-muted-foreground">{{ $t('尚未创建测试容器。') }}</p>

      <div class="flex flex-wrap items-center gap-2">
        <Button
          v-if="canStart"
          type="button"
          :disabled="busy || definitionDirty"
          @click="start"
        >
          <Spinner v-if="busy" data-icon="inline-start" />{{ $t('启动测试容器') }}
        </Button>
        <Button v-else-if="runtime?.state === 'Stopping'" type="button" disabled>
          <Spinner data-icon="inline-start" />{{ $t('停止中') }}
        </Button>
        <Button v-if="active" type="button" variant="outline" :disabled="busy" @click="stop">
          {{ $t('停止') }}
        </Button>
        <Button
          v-if="active"
          type="button"
          variant="outline"
          :disabled="busy || definitionDirty"
          @click="reset"
        >
          <Spinner v-if="busy" data-icon="inline-start" />{{ $t('重置测试容器') }}
        </Button>
        <div v-if="canExtend" class="flex items-center gap-2">
          <Input v-model.number="extendMinutes" type="number" min="1" max="1440" class="w-20" :aria-label="$t('续期分钟数')" />
          <Button type="button" variant="outline" :disabled="busy" @click="extend">{{ $t('续期(分钟)') }}</Button>
        </div>
      </div>
    </template>
  </section>
</template>
