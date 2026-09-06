<script setup lang="ts">
import { Lightbulb, LockKeyhole } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { getChallengeEndpoint, getGameplayFactStatusEndpoint, unlockChallengeHintEndpoint } from '~/api'
import type { NoCtfapiEndpointsChallengesParticipantChallengeHintResponse } from '~/api'
import { affectsChallengeHints, hintUnlockState, readableHintContent } from '~/lib/challenge-hints'
import { createLatestRequestGuard } from '~/lib/latest-request'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'

type Hint = NoCtfapiEndpointsChallengesParticipantChallengeHintResponse
const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  hints?: Hint[] | null
}>()
const emit = defineEmits<{ unlocked: [] }>()
const { isLoggedIn } = useAuth()
const headingId = useId()
const hints = ref<Hint[]>(props.hints ?? [])
const refreshing = ref(false)
const refreshError = ref<string | null>(null)
const unlockError = ref<string | null>(null)
const confirmingId = ref<string | null>(null)
const submitting = ref(false)
const pendingFactId = ref<string | null>(null)
const requestGuard = createLatestRequestGuard()
const busy = computed(() => submitting.value || pendingFactId.value !== null)
let disposed = false

watch(() => props.hints, value => { hints.value = value ?? [] })

async function loadHints(): Promise<void> {
  const request = requestGuard.begin()
  refreshing.value = true
  try {
    const { data, error } = await getChallengeEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
    })
    if (!requestGuard.isCurrent(request)) return
    if (error || !data) throw parseApiError(error, translate('刷新提示失败'))
    hints.value = data.hints ?? []
    refreshError.value = null
  }
  catch (error) {
    if (requestGuard.isCurrent(request)) refreshError.value = parseApiError(error, translate('刷新提示失败')).message
  }
  finally {
    if (requestGuard.isCurrent(request)) refreshing.value = false
  }
}
const refreshHints = createTrailingRefresh(loadHints)

async function checkUnlock(): Promise<boolean> {
  const id = pendingFactId.value
  if (!id) return true
  const { data, error } = await getGameplayFactStatusEndpoint({
    path: { competitionId: props.competitionId, gameplayFactId: id },
  })
  if (disposed || id !== pendingFactId.value) return true
  if (error || !data) throw parseApiError(error, translate('刷新提示解锁状态失败'))
  const state = hintUnlockState(data)
  if (state === 'pending') return false
  pendingFactId.value = null
  if (state === 'unlocked') {
    await refreshHints()
    if (disposed) return true
    toast.success(translate('提示已解锁'))
    emit('unlocked')
  }
  else {
    unlockError.value = data.failureCode
      ? gameplayFactFailureCodeLabel(data.failureCode)
      : translate('解锁提示失败')
  }
  return true
}

const { start, stop, error: pollingError, timedOut } = usePolling(checkUnlock, { timeout: 45_000 })

async function unlock(hint: Hint): Promise<void> {
  if (!hint.id || !hint.canUnlock || busy.value || confirmingId.value !== hint.id) return
  submitting.value = true
  unlockError.value = null
  try {
    const { data, error } = await unlockChallengeHintEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, hintId: hint.id },
    })
    if (disposed) return
    if (error || !data?.gameplayFactId) throw parseApiError(error, translate('解锁提示失败'))
    confirmingId.value = null
    pendingFactId.value = data.gameplayFactId
    start()
  }
  catch (error) {
    if (!disposed) unlockError.value = parseApiError(error, translate('解锁提示失败')).message
  }
  finally {
    if (!disposed) submitting.value = false
  }
}

function retry(): void {
  unlockError.value = null
  void refreshHints()
  if (pendingFactId.value) start()
}

let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    competitionEventChanged: event => {
      if (affectsChallengeHints(event.kind)) void refreshHints()
    },
    gameplayFactStateChanged: fact => {
      if (fact.kind === 'HintUnlock' && fact.competitionChallengeId === props.competitionChallengeId)
        void refreshHints()
    },
    onReconnected: () => void refreshHints(),
  })
})
onUnmounted(() => {
  disposed = true
  requestGuard.invalidate()
  unwatch?.()
  stop()
})
</script>

<template>
  <section class="py-5" :aria-labelledby="headingId" aria-live="polite">
    <div class="flex items-center justify-between gap-3">
      <h3 :id="headingId" class="flex items-center gap-2 text-sm font-semibold">
        <Lightbulb class="size-4 text-primary" />{{ $t('题目提示') }}
      </h3>
      <Button type="button" variant="ghost" size="sm" :disabled="refreshing" @click="retry">
        <Spinner v-if="refreshing" data-icon="inline-start" />{{ $t('刷新提示') }}
      </Button>
    </div>
    <Alert v-if="refreshError || unlockError || pollingError || timedOut" variant="destructive" class="mt-3">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ refreshError ?? unlockError ?? (timedOut ? $t('提示解锁尚未完成，请重新加载。') : $t('刷新提示解锁状态失败')) }}</span>
        <Button type="button" variant="outline" size="sm" @click="retry">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <p v-else-if="hints.length === 0" class="mt-3 text-sm text-muted-foreground">{{ $t('暂无已发布的提示') }}</p>
    <p v-if="pendingFactId && !timedOut" class="mt-3 flex items-center gap-2 text-sm text-muted-foreground">
      <Spinner class="size-4" />{{ $t('正在解锁提示') }}
    </p>
    <ul v-if="hints.length" class="mt-2">
      <li v-for="(hint, index) in hints" :key="hint.id ?? index" class="py-3">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <span class="text-sm font-medium">{{ $t('提示 {index}', { index: index + 1 }) }}</span>
          <Badge v-if="hint.isUnlocked" variant="secondary">{{ (hint.cost ?? 0) === 0 ? $t('免费') : $t('已解锁') }}</Badge>
          <Button v-else type="button" variant="outline" size="sm" :disabled="!hint.canUnlock || busy" @click="confirmingId = hint.id ?? null">
            <LockKeyhole data-icon="inline-start" />{{ $t('解锁提示（{cost} 分）', { cost: hint.cost ?? 0 }) }}
          </Button>
        </div>
        <p v-if="readableHintContent(hint) !== null" class="mt-2 whitespace-pre-wrap break-words text-sm leading-6">{{ readableHintContent(hint) }}</p>
        <p v-else-if="!hint.canUnlock" class="mt-2 text-sm text-muted-foreground">
          {{ isLoggedIn ? $t('当前状态无法解锁提示') : $t('登录后可解锁提示') }}
        </p>
        <div v-if="confirmingId === hint.id && !pendingFactId" class="mt-3 flex flex-wrap items-center gap-3">
          <p class="text-sm">{{ $t('解锁此提示将扣除本队 {cost} 分。', { cost: hint.cost ?? 0 }) }}</p>
          <Button type="button" size="sm" :disabled="busy" @click="unlock(hint)">
            <Spinner v-if="submitting" data-icon="inline-start" />{{ $t('确认解锁') }}
          </Button>
          <Button type="button" variant="ghost" size="sm" :disabled="busy" @click="confirmingId = null">{{ $t('取消') }}</Button>
        </div>
        <Separator v-if="index < hints.length - 1" class="mt-4" />
      </li>
    </ul>
  </section>
  <Separator />
</template>
