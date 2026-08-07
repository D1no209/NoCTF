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

type Runtime = NoCtfapiEndpointsRuntimeRuntimeResponse

const props = withDefaults(
  defineProps<{
    competitionId: string
    competitionChallengeId: string
    /** full = CTF 全操作;reset-only = AWD 仅重置 */
    controls?: 'full' | 'reset-only'
  }>(),
  { controls: 'full' },
)

const runtime = ref<Runtime | null>(null)
const loading = ref(true)
const acting = ref(false)
const extendMinutes = ref(30)

const TRANSITIONAL: string[] = [RuntimeState.Queued, RuntimeState.Provisioning, RuntimeState.Stopping]

async function load(): Promise<void> {
  const { data, error } = await getRuntimeEndpoint({
    path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
  })
  runtime.value = error || !data ? null : data
}

onMounted(async () => {
  await load()
  loading.value = false
  // 初始加载时若处于过渡态,继续轮询直到稳定
  if (runtime.value && TRANSITIONAL.includes(runtime.value.state ?? '')) startPolling()
})

const { polling, timedOut, start: startPolling } = usePolling(
  async () => {
    await load()
    return !runtime.value || !TRANSITIONAL.includes(runtime.value.state ?? '')
  },
  { interval: 2000, timeout: 120_000 },
)

async function act(action: () => Promise<{ error?: unknown }>, failMessage: string) {
  acting.value = true
  const { error } = await action()
  acting.value = false
  if (error) {
    toast.error(parseApiError(error, failMessage).message)
    return
  }
  toast.success('操作已受理,环境状态更新中')
  startPolling()
}

const path = computed(() => ({
  competitionId: props.competitionId,
  competitionChallengeId: props.competitionChallengeId,
}))

const start = () => act(() => startRuntimeEndpoint({ path: path.value }), '启动环境失败')
const stop = () => act(() => stopRuntimeEndpoint({ path: path.value }), '停止环境失败')
const reset = () => act(() => resetRuntimeEndpoint({ path: path.value }), '重置环境失败')
const extend = () =>
  act(
    () =>
      extendRuntimeEndpoint({
        path: path.value,
        body: { seconds: Math.max(60, Math.round(extendMinutes.value * 60)) },
      }),
    '续期失败',
  )

// TTL 倒计时
const now = ref(Date.now())
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
  return remaining > 0 ? formatDuration(remaining) : '已到期'
})

const isRunning = computed(() => runtime.value?.state === RuntimeState.Running)
const busy = computed(() => acting.value || polling.value)
const stateVariant = computed(() => {
  switch (runtime.value?.state) {
    case RuntimeState.Running:
      return 'default' as const
    case RuntimeState.Failed:
      return 'destructive' as const
    case RuntimeState.Stopped:
      return 'secondary' as const
    default:
      return 'outline' as const
  }
})
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center justify-between gap-2">
        <CardTitle class="text-base">题目环境</CardTitle>
        <Badge v-if="runtime" :variant="stateVariant">
          {{ runtimeStateLabel(runtime.state) }}
        </Badge>
      </div>
      <CardDescription v-if="controls === 'reset-only'">
        AWD 模式下环境由平台统一发放,仅支持重置
      </CardDescription>
    </CardHeader>
    <CardContent class="flex flex-col gap-4">
      <Skeleton v-if="loading" class="h-16 w-full" />

      <template v-else>
        <Alert v-if="timedOut">
          <AlertDescription>环境状态更新超时,请稍后手动刷新。</AlertDescription>
        </Alert>

        <div v-if="!runtime" class="text-sm text-muted-foreground">
          {{ controls === 'full' ? '环境尚未启动,点击「启动环境」获取你的专属实例。' : '平台尚未为本队发放环境。' }}
        </div>

        <template v-else>
          <div v-if="isRunning && runtime.urls?.length" class="flex flex-col gap-1">
            <span class="text-sm text-muted-foreground">访问地址</span>
            <a
              v-for="url in runtime.urls"
              :key="url"
              :href="url"
              target="_blank"
              rel="noopener"
              class="break-all font-mono text-sm text-primary underline"
            >
              {{ url }}
            </a>
          </div>

          <div v-if="isRunning && ttl" class="text-sm">
            剩余时间:<span class="font-medium">{{ ttl }}</span>
          </div>
        </template>

        <div class="flex flex-wrap items-center gap-2">
          <template v-if="controls === 'full'">
            <Button v-if="!runtime || runtime.state === RuntimeState.Stopped || runtime.state === RuntimeState.Failed" :disabled="busy" @click="start">
              <Spinner v-if="busy && polling" data-icon="inline-start" />
              启动环境
            </Button>
            <Button v-if="isRunning" variant="outline" :disabled="busy" @click="stop">
              停止
            </Button>
          </template>
          <Button v-if="runtime" variant="outline" :disabled="busy" @click="reset">
            <Spinner v-if="polling" data-icon="inline-start" />
            重置环境
          </Button>
          <template v-if="controls === 'full' && isRunning">
            <div class="flex items-center gap-2">
              <Input
                v-model.number="extendMinutes"
                type="number"
                min="1"
                max="720"
                class="w-20"
                aria-label="续期分钟数"
              />
              <Button variant="outline" :disabled="busy" @click="extend">续期(分钟)</Button>
            </div>
          </template>
        </div>
      </template>
    </CardContent>
  </Card>
</template>
