<script setup lang="ts">
import { toast } from 'vue-sonner'
import { getGameplayFactStatusEndpoint, submitFlagEndpoint } from '~/api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '~/api'

type TrackedSubmission = Pick<
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
  'state' | 'result' | 'failureCode'
> & {
  id: string
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
  { multiple: false, title: translate("提交 Flag"), description: '' },
)

const emit = defineEmits<{ evaluated: [] }>()

const input = ref('')
const submitting = ref(false)
const tracked = ref<TrackedSubmission[]>([])
const toasted = new Set<string>()
const celebrating = ref(false)
let celebrationTimer: ReturnType<typeof setTimeout> | undefined

function celebrateCorrectFlag(): void {
  celebrating.value = false
  if (celebrationTimer) clearTimeout(celebrationTimer)
  requestAnimationFrame(() => {
    celebrating.value = true
    celebrationTimer = setTimeout(() => {
      celebrating.value = false
    }, 650)
  })
}

const { polling, timedOut, start: startPolling, stop: stopPolling } = usePolling(
  async () => {
    await refreshPending()
    return !tracked.value.some((t) => isGameplayFactPending(t.state))
  },
  { interval: 1500, timeout: 120_000 },
)

async function refreshOne(id: string): Promise<void> {
  const { data, error } = await getGameplayFactStatusEndpoint({
    path: { competitionId: props.competitionId, gameplayFactId: id },
  })
  if (error || !data) return
  const item = tracked.value.find((t) => t.id === id)
  const wasPending = item ? isGameplayFactPending(item.state) : true
  if (item) {
    item.state = data.state
    item.result = data.result
    item.failureCode = data.failureCode
  }
  else {
    tracked.value.unshift({
      id,
      state: data.state,
      result: data.result,
      failureCode: data.failureCode,
    })
  }
  if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id)) {
    toasted.add(id)
    if (data.result === 'Correct') {
      toast.success(translate("提交评测完成:正确"))
      celebrateCorrectFlag()
    }
    else toast.error(translate('提交评测完成：{result}', { result: gameplayFactResultLabel(data.result) }))
    emit('evaluated')
  }
}

async function refreshPending(): Promise<void> {
  const pending = tracked.value.filter((t) => isGameplayFactPending(t.state))
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
    toast.error(parseApiError(error, translate("提交失败")).message)
    return
  }
  const ids = [
    ...(data.gameplayFactId ? [data.gameplayFactId] : []),
    ...(data.submissions ?? []).map((s) => s.gameplayFactId).filter((id): id is string => !!id),
  ]
  for (const id of ids) {
    if (!tracked.value.some((t) => t.id === id)) {
      tracked.value.unshift({ id, state: 'Pending', result: null })
    }
  }
  input.value = ''
  toast.success(translate('已受理 {count} 条提交，评测中', { count: ids.length }))
  startPolling()
}

// 实时:服务端推送提交结果 → 立即刷新该提交
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    gameplayFactStateChanged: (payload) => {
      const id = (payload as { gameplayFactId?: unknown })?.gameplayFactId
      if (typeof id === 'string' && tracked.value.some((t) => t.id === id)) {
        void refreshOne(id)
      }
    },
  })
})
onUnmounted(() => {
  unwatch?.()
  stopPolling()
  if (celebrationTimer) clearTimeout(celebrationTimer)
})

function resultVariant(result?: string | null) {
  if (result === 'Correct') return 'default' as const
  return 'destructive' as const
}
</script>

<template>
  <Card class="relative overflow-hidden">
    <Transition name="flag-celebration">
      <div
        v-if="celebrating"
        role="status"
        aria-live="polite"
        class="pointer-events-none absolute inset-0 z-10 grid place-items-center bg-background/70"
      >
        <span class="sr-only">{{ $t('Flag 正确') }}</span>
        <span aria-hidden="true" class="flag-celebration-mark">🎉</span>
      </div>
    </Transition>
    <CardHeader>
      <CardTitle class="text-base">{{ title }}</CardTitle>
      <CardDescription v-if="description">{{ description }}</CardDescription>
    </CardHeader>
    <CardContent>
      <form @submit.prevent="submit">
        <FieldGroup>
          <Field>
            <FieldLabel :for="`flag-input-${competitionChallengeId}`">
              {{ multiple ? $t('Flag 列表(每行一个)') : 'Flag' }}
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
              <Spinner v-if="submitting" data-icon="inline-start" /> {{ $t('提交') }} </Button>
          </Field>
        </FieldGroup>
      </form>

      <div v-if="tracked.length" class="mt-4 flex flex-col gap-2">
        <Alert v-if="timedOut">
          <AlertDescription>{{ $t('评测结果等待超时,可稍后在「我的提交」查看结果。') }}</AlertDescription>
        </Alert>
        <Alert
          v-for="item in tracked"
          :key="item.id"
          :variant="isGameplayFactPending(item.state) ? 'default' : resultVariant(item.result)"
        >
          <AlertDescription class="flex items-center gap-2">
            <Spinner v-if="isGameplayFactPending(item.state)" class="size-3" />
            <span v-if="isGameplayFactPending(item.state)">
              {{ gameplayFactStateLabel(item.state) }}…
            </span>
            <span v-else> {{ $t('评测结果:') }}<strong>{{ gameplayFactResultLabel(item.result) }}</strong>
            </span>
          </AlertDescription>
        </Alert>
      </div>
    </CardContent>
  </Card>
</template>

<style scoped>
.flag-celebration-mark {
  font-size: 4rem;
  animation: flag-celebration-mark 420ms cubic-bezier(0.16, 1, 0.3, 1) both;
}

.flag-celebration-leave-active {
  transition: opacity 160ms cubic-bezier(0.25, 1, 0.5, 1);
}

.flag-celebration-leave-to {
  opacity: 0;
}

@keyframes flag-celebration-mark {
  0% { opacity: 0; transform: translateY(0.75rem) scale(0.72) rotate(-8deg); }
  45% { opacity: 1; transform: translateY(0) scale(1.05) rotate(3deg); }
  100% { opacity: 1; transform: translateY(-0.25rem) scale(1) rotate(0); }
}

@media (prefers-reduced-motion: reduce) {
  .flag-celebration-mark {
    animation: none;
  }

  .flag-celebration-leave-active {
    transition: none;
  }
}
</style>
