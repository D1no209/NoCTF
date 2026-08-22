<script setup lang="ts">
import { History, ShieldCheck } from '@lucide/vue'
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

const state = ref<NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse | null>(null)
const loading = ref(true)

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
  if (error || !data) return true
  state.value = data
  return !defenseTransitionInProgress.value
}

const { start: startStatePolling, stop: stopStatePolling } = usePolling(
  refreshState,
  { interval: 1500, timeout: 300_000 },
)

async function refreshAndPoll(): Promise<void> {
  await refreshState()
  if (defenseTransitionInProgress.value) startStatePolling()
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
          class="border-b pb-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          controls="full"
        />

        <Alert v-if="state?.breakActivation" class="border-emerald-500/40 bg-emerald-500/5">
          <ShieldCheck class="text-emerald-600 dark:text-emerald-400" />
          <AlertTitle>{{ $t('攻击已锁定，后续 Flag 不再受理') }}</AlertTitle>
        </Alert>

        <FlagSubmit
          v-else
          class="border-t pt-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :title="$t('提交 Flag')"
          @evaluated="refreshAndPoll"
        />
      </section>

      <section class="flex min-w-0 flex-col gap-5 border-t pt-6 xl:border-l xl:border-t-0 xl:pl-6 xl:pt-0" aria-labelledby="awdp-defense-title">
        <header class="flex flex-wrap items-center justify-between gap-3 border-b pb-4">
          <div class="flex flex-wrap items-center gap-2">
            <h2 id="awdp-defense-title" class="text-lg font-semibold">{{ $t('防御轨 · Fix') }}</h2>
            <Badge v-if="state?.fixActivation" variant="default">
              {{ $t('已于第 {round} 轮生效', { round: state.fixActivation.effectiveRound ?? '-' }) }}
            </Badge>
          </div>
          <Button as-child variant="outline" size="sm">
            <NuxtLink :to="`/competitions/${competition.id}/challenges/${challenge.id}/fix-history`">
              <History data-icon="inline-start" />
              {{ $t('Fix 历史') }}
            </NuxtLink>
          </Button>
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
          @accepted="refreshAndPoll"
        />
      </section>
    </div>
  </div>
</template>
