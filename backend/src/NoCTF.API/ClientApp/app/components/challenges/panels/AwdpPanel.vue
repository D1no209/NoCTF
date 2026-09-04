<script setup lang="ts">
import { ShieldCheck } from '@lucide/vue'
import { getAwdpParticipantStateEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse,
} from '~/api'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const emit = defineEmits<{ submitted: [] }>()

const state = ref<NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse | null>(null)
const loading = ref(true)
const stateError = ref<string | null>(null)
const attackRuntimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

const defenseTransitionInProgress = computed(() => {
  const factState = state.value?.defense?.state
  const runtimeState = state.value?.defense?.runtimeState
  return factState === 'Pending'
    || factState === 'Queued'
    || factState === 'Processing'
    || runtimeState === 'Queued'
    || runtimeState === 'Provisioning'
    || runtimeState === 'Stopping'
})

const defenseOutcome = computed(() => {
  const defense = state.value?.defense
  if (!defense?.gameplayFactId || !defense.result && !defense.failureCode) return null
  if (defense.result === 'Correct') return translate('防御成功')
  if (defense.failureCode === 'AwdpExploitSucceeded') return translate('防御异常：EXP 利用成功')
  if (defense.failureCode === 'AwdpServiceAbnormal') return translate('防御异常：服务异常')
  if (defense.state === 'PlatformFailed') return translate('防御验证失败')
  return translate('防御异常：服务异常')
})

async function refreshState(): Promise<boolean> {
  const { data, error } = await getAwdpParticipantStateEndpoint({
    path: {
      competitionId: props.competition.id!,
      competitionChallengeId: props.challenge.id!,
    },
  })
  loading.value = false
  if (error || !data) {
    stateError.value = parseApiError(error, translate('加载 AWDP 题目状态失败')).message
    return false
  }
  stateError.value = null
  state.value = data
  return !defenseTransitionInProgress.value
}

const { timedOut: statePollingTimedOut, start: startStatePolling, stop: stopStatePolling } = usePolling(
  refreshState,
  { interval: 1500, timeout: 300_000 },
)

async function refreshAndPoll(): Promise<void> {
  const complete = await refreshState()
  if (!complete) startStatePolling()
}

async function handleBreakEvaluation(result?: string | null): Promise<void> {
  await refreshAndPoll()
  if (result === 'Correct') await attackRuntimeCard.value?.refreshUntilStopped()
}

async function handleFixAccepted(): Promise<void> {
  emit('submitted')
  await refreshAndPoll()
}

let unwatch: (() => void) | undefined
onMounted(() => {
  void refreshAndPoll()
  unwatch = watchCompetition(props.competition.id!, {
    gameplayFactStateChanged: () => void refreshAndPoll(),
  })
})
onUnmounted(() => {
  unwatch?.()
  stopStatePolling()
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="statePollingTimedOut" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('AWDP 状态自动刷新已停止，请手动重试。') }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshAndPoll">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="stateError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ stateError }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshAndPoll">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <div v-if="loading" class="grid gap-6 lg:grid-cols-2">
      <Skeleton class="h-80 w-full" />
      <Skeleton class="h-80 w-full" />
    </div>

    <div v-else class="grid items-start gap-6 xl:grid-cols-2 xl:gap-0">
      <section class="flex min-w-0 flex-col gap-5 xl:pr-6" aria-labelledby="awdp-attack-title">
        <header class="flex flex-wrap items-center justify-between gap-3 border-b pb-4">
          <div class="flex flex-wrap items-center gap-2">
            <h2 id="awdp-attack-title" class="text-lg font-semibold">{{ $t('攻击靶机 · Break 环境') }}</h2>
            <Badge v-if="state?.breakActivation" variant="default">
              {{ $t('已于第 {round} 轮生效', { round: state.breakActivation.effectiveRound ?? '-' }) }}
            </Badge>
          </div>
        </header>

        <RuntimeCard
          ref="attackRuntimeCard"
          class="border-b pb-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :controls="state?.breakActivation ? 'readonly' : 'full'"
        />

        <FlagSubmit
          class="border-t pt-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :title="state?.breakActivation ? $t('验证 Flag') : $t('提交 Flag')"
          :read-only-judgement="!!state?.breakActivation"
          :maximum-attempts="challenge.maximumFlagAttempts"
          :remaining-attempts="challenge.remainingFlagAttempts"
          @evaluated="handleBreakEvaluation"
          @submitted="emit('submitted')"
        />
      </section>

      <section class="flex min-w-0 flex-col gap-5 border-t pt-6 xl:border-l xl:border-t-0 xl:pl-6 xl:pt-0" aria-labelledby="awdp-defense-title">
        <header class="flex flex-wrap items-center gap-3 border-b pb-4">
          <div class="flex flex-wrap items-center gap-2">
            <h2 id="awdp-defense-title" class="text-lg font-semibold">{{ $t('防御轨 · Fix') }}</h2>
            <Badge v-if="state?.fixActivation" variant="default">
              {{ $t('已于第 {round} 轮生效', { round: state.fixActivation.effectiveRound ?? '-' }) }}
            </Badge>
            <Badge
              v-if="state?.maximumFixAttempts !== null && state?.maximumFixAttempts !== undefined"
              variant="secondary"
              class="font-mono tabular-nums"
            >
              {{ $t('剩余 {count} 次提交', { count: state.remainingFixAttempts ?? 0 }) }}
            </Badge>
          </div>
        </header>

        <section v-if="state?.defense?.gameplayFactId" class="border-b pb-5" aria-labelledby="awdp-last-fix-title">
            <h3 id="awdp-last-fix-title" class="mb-3 text-sm font-semibold">{{ $t('最近一次 Fix 验证') }}</h3>
            <div class="flex flex-wrap items-center justify-between gap-3 text-sm">
              <Badge v-if="state.defense.state" variant="outline">
                {{ gameplayFactStateLabel(state.defense.state) }}
              </Badge>
              <strong v-if="defenseOutcome" :class="state.defense.result === 'Correct' ? 'text-emerald-600 dark:text-emerald-400' : 'text-destructive'">
                {{ defenseOutcome }}
              </strong>
            </div>
        </section>

        <Alert v-if="state?.fixActivation" class="border-sky-500/40 bg-sky-500/5">
          <ShieldCheck class="text-sky-600 dark:text-sky-400" />
          <AlertTitle>{{ $t('防御已锁定，无需再次申请防御验证') }}</AlertTitle>
        </Alert>

        <FixSubmit
          v-else
          class="border-t pt-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :defense="state?.defense"
          @changed="refreshAndPoll"
          @accepted="handleFixAccepted"
        />
      </section>
    </div>

  </div>
</template>
