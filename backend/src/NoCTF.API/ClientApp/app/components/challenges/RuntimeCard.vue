<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  extendRuntimeEndpoint,
  getRuntimeEndpoint,
  resetRuntimeEndpoint,
  startRuntimeEndpoint,
  stopRuntimeEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsRuntimeRuntimeResponse } from '~/api'
import {
  classifyPlayerRuntimeLookup,
  normalizePlayerRuntime,
  shouldPollPlayerRuntime,
  type PlayerRuntimeLookupOutcome,
} from '~/utils/player-runtime'

type Runtime = NoCtfapiEndpointsRuntimeRuntimeResponse

const props = withDefaults(
  defineProps<{
    competitionId: string
    competitionChallengeId: string
    /** full = CTF 全操作;reset-only = AWD 仅重置;readonly = 只显示最终状态 */
    controls?: 'full' | 'reset-only' | 'readonly'
  }>(),
  { controls: 'full' },
)

const runtime = ref<Runtime | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const acting = ref(false)
const extendMinutes = ref(30)
const now = ref(Date.now())
const forceUntilStopped = ref(false)

async function load(): Promise<PlayerRuntimeLookupOutcome> {
  const { data, error, response } = await getRuntimeEndpoint({
    path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
  })
  const outcome = classifyPlayerRuntimeLookup(response?.status, Boolean(error), Boolean(data))
  if (outcome === 'missing') {
    runtime.value = null
    loadError.value = null
    return outcome
  }
  if (outcome === 'failed') {
    loadError.value = parseApiError(error, translate("加载环境状态失败")).message
    return outcome
  }

  runtime.value = normalizePlayerRuntime(data ?? null)
  loadError.value = null
  return outcome
}

onMounted(async () => {
  const outcome = await load()
  loading.value = false
  if (outcome === 'available' && shouldPollPlayerRuntime(runtime.value, now.value)) startPolling()
})

const { polling, timedOut, start: startPolling } = usePolling(
  async () => {
    const outcome = await load()
    if (forceUntilStopped.value) {
      if (outcome === 'failed' || runtime.value?.state !== 'Running') {
        forceUntilStopped.value = false
        return true
      }
      return false
    }
    return outcome === 'failed' || !shouldPollPlayerRuntime(runtime.value, now.value)
  },
  { interval: 2000, timeout: 120_000 },
)

async function refreshUntilStopped(): Promise<void> {
  forceUntilStopped.value = true
  const outcome = await load()
  if (outcome === 'failed' || runtime.value?.state !== 'Running') {
    forceUntilStopped.value = false
    return
  }
  startPolling()
}

defineExpose({ refreshUntilStopped })

watch(
  () => shouldPollPlayerRuntime(runtime.value, now.value),
  (needsPolling) => {
    if (!loadError.value && needsPolling && runtime.value?.state === 'Running' && !polling.value)
      startPolling()
  },
)

async function retryLoad(): Promise<void> {
  loading.value = true
  const outcome = await load()
  loading.value = false
  if (outcome === 'available' && shouldPollPlayerRuntime(runtime.value, now.value))
    startPolling()
}

async function act(action: () => Promise<{ error?: unknown }>, failMessage: string) {
  acting.value = true
  const { error } = await action()
  acting.value = false
  if (error) {
    toast.error(parseApiError(error, failMessage).message)
    return
  }
  toast.success(translate("操作已受理,环境状态更新中"))
  startPolling()
}

const path = computed(() => ({
  competitionId: props.competitionId,
  competitionChallengeId: props.competitionChallengeId,
}))

const start = () => act(() => startRuntimeEndpoint({ path: path.value }), translate("启动环境失败"))
const stop = () => act(() => stopRuntimeEndpoint({ path: path.value }), translate("停止环境失败"))
const reset = () => act(() => resetRuntimeEndpoint({ path: path.value }), translate("重置环境失败"))
const extend = () =>
  act(
    () =>
      extendRuntimeEndpoint({
        path: path.value,
        body: { seconds: Math.max(60, Math.round(extendMinutes.value * 60)) },
      }),
    translate("续期失败"),
  )

// TTL 倒计时
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  timer = setInterval(() => {
    now.value = Date.now()
  }, 1000)
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
})

const ttl = computed(() => {
  if (!runtime.value?.expiresAt) return null
  const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
  return remaining > 0 ? formatDuration(remaining) : translate("已到期")
})

const isRunning = computed(() => runtime.value?.state === 'Running')
const busy = computed(() => acting.value || polling.value)
const stateVariant = computed(() => {
  switch (runtime.value?.state) {
    case 'Running':
      return 'default' as const
    case 'Failed':
      return 'destructive' as const
    case 'Stopped':
      return 'secondary' as const
    default:
      return 'outline' as const
  }
})
</script>

<template>
  <section class="flex flex-col gap-4" aria-labelledby="runtime-card-title">
    <header class="flex items-center justify-between gap-2">
        <h3 id="runtime-card-title" class="text-sm font-semibold">{{ $t('题目环境') }}</h3>
        <Badge v-if="runtime" :variant="stateVariant">
          {{ runtimeStateLabel(runtime.state) }}
        </Badge>
    </header>
    <div class="flex flex-col gap-4">
      <Skeleton v-if="loading" class="h-16 w-full" />

      <template v-else>
        <Alert v-if="loadError" variant="destructive">
          <AlertDescription class="flex flex-wrap items-center justify-between gap-2">
            <span>{{ loadError }}</span>
            <Button size="sm" variant="outline" @click="retryLoad">{{ $t('重试') }}</Button>
          </AlertDescription>
        </Alert>

        <Alert v-else-if="timedOut">
          <AlertDescription>{{ $t('环境状态更新超时,请稍后手动刷新。') }}</AlertDescription>
        </Alert>

        <template v-if="!loadError && runtime">
          <div v-if="isRunning && runtime.urls?.length" class="flex flex-col gap-1">
            <span class="text-sm text-muted-foreground">{{ $t('访问地址') }}</span>
            <RuntimeAccessUrl
              v-for="url in runtime.urls"
              :key="url"
              :url="url"
            />
          </div>

          <div v-if="isRunning && ttl" class="text-sm"> {{ $t('剩余时间:') }}<span class="font-mono font-medium tabular-nums">{{ ttl }}</span>
          </div>
        </template>

        <div v-if="!loadError" class="flex flex-wrap items-center gap-2">
          <template v-if="controls === 'full'">
            <Button v-if="!runtime || runtime.state === 'Stopped' || runtime.state === 'Failed'" :disabled="busy" @click="start">
              <Spinner v-if="busy && polling" data-icon="inline-start" /> {{ $t('启动环境') }} </Button>
            <Button v-if="isRunning" variant="outline" :disabled="busy" @click="stop"> {{ $t('停止') }} </Button>
          </template>
          <Button v-if="runtime && controls !== 'readonly'" variant="outline" :disabled="busy" @click="reset">
            <Spinner v-if="polling" data-icon="inline-start" /> {{ $t('重置环境') }} </Button>
          <template v-if="controls === 'full' && isRunning">
            <div class="flex items-center gap-2">
              <Input
                v-model.number="extendMinutes"
                type="number"
                min="1"
                max="720"
                class="w-20"
                :aria-label="$t('续期分钟数')"
              />
              <Button variant="outline" :disabled="busy" @click="extend">{{ $t('续期(分钟)') }}</Button>
            </div>
          </template>
        </div>
      </template>
    </div>
  </section>
</template>
