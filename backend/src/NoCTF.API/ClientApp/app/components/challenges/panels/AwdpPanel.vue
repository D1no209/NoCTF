<script setup lang="ts">
import { getAwdpParticipantStateEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsGameplayFactsAwdpFixStageProtocol,
  NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse,
} from '~/api'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const state = ref<NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

const fixStages = [
  'TargetProvisioning',
  'PatchApplying',
  'CheckerRunning',
  'Completed',
] as const satisfies readonly NoCtfapiEndpointsGameplayFactsAwdpFixStageProtocol[]

const fixStageLabels: Record<NoCtfapiEndpointsGameplayFactsAwdpFixStageProtocol, string> = {
  TargetProvisioning: '创建干净验证环境',
  PatchApplying: '应用补丁',
  CheckerRunning: '执行一次 Checker',
  Completed: '验证完成',
}

const activeFixStageIndex = computed(() => {
  const stage = state.value?.defense?.stage
  return stage ? fixStages.indexOf(stage) : -1
})

const fixInProgress = computed(() => {
  const factState = state.value?.defense?.state
  return factState === 'Pending' || factState === 'Queued' || factState === 'Processing'
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
    loadError.value = parseApiError(error, translate('加载 AWDP 状态失败')).message
    return true
  }
  state.value = data
  loadError.value = null
  return !fixInProgress.value
}

const { start: startStatePolling, stop: stopStatePolling } = usePolling(
  refreshState,
  { interval: 1500, timeout: 300_000 },
)

async function refreshAndPoll(): Promise<void> {
  await refreshState()
  if (fixInProgress.value) startStatePolling()
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
    <Alert>
      <AlertDescription>
        {{ $t('AWDP 使用本队独立攻击实例承载动态 Flag；Fix 始终在独立的干净环境中进行一次性验证。') }}
      </AlertDescription>
    </Alert>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-2">
        <span>{{ loadError }}</span>
        <Button size="sm" variant="outline" @click="refreshAndPoll">{{ $t('重试') }}</Button>
      </AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-6 lg:grid-cols-2">
      <Skeleton class="h-80 w-full" />
      <Skeleton class="h-80 w-full" />
    </div>

    <div v-else class="grid items-start gap-6 lg:grid-cols-2">
      <section class="flex min-w-0 flex-col gap-4" aria-labelledby="awdp-attack-title">
        <div>
          <div class="mb-1 flex flex-wrap items-center gap-2">
            <h2 id="awdp-attack-title" class="text-lg font-semibold">{{ $t('攻击轨 · Break') }}</h2>
            <Badge v-if="state?.breakActivation" variant="default">
              {{ $t('已于第 {round} 轮生效', { round: state.breakActivation.effectiveRound ?? '-' }) }}
            </Badge>
          </div>
          <p class="text-sm text-muted-foreground">
            {{ $t('创建本队独立攻击实例，利用漏洞取得当前 Runtime generation 的动态 Flag。每轮首次正确 Break 按本轮独立动态分值结算。') }}
          </p>
        </div>

        <RuntimeCard
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          controls="full"
        />

        <FlagSubmit
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :title="$t('Break · 提交动态 Flag')"
          :description="$t('提交当前攻击实例中取得的单个动态 Flag。重置实例后，旧 generation 的 Flag 会立即失效。')"
          @evaluated="refreshAndPoll"
        />
      </section>

      <section class="flex min-w-0 flex-col gap-4" aria-labelledby="awdp-defense-title">
        <div>
          <div class="mb-1 flex flex-wrap items-center gap-2">
            <h2 id="awdp-defense-title" class="text-lg font-semibold">{{ $t('防御轨 · Fix') }}</h2>
            <Badge v-if="state?.fixActivation" variant="default">
              {{ $t('已于第 {round} 轮生效', { round: state.fixActivation.effectiveRound ?? '-' }) }}
            </Badge>
          </div>
          <p class="text-sm text-muted-foreground">
            {{ $t('平台会创建全新的干净验证环境、应用补丁，并只执行一次 Checker；本题也可以要求完成 Break 后才允许提交 Fix。') }}
          </p>
        </div>

        <Card v-if="state?.defense?.gameplayFactId">
          <CardHeader>
            <CardTitle class="text-base">{{ $t('最近一次 Fix 验证') }}</CardTitle>
            <CardDescription v-if="state.currentRound">
              {{ $t('当前逻辑轮次：第 {round} 轮', { round: state.currentRound }) }}
            </CardDescription>
          </CardHeader>
          <CardContent class="flex flex-col gap-4">
            <ol class="grid gap-2 sm:grid-cols-2">
              <li
                v-for="(stage, index) in fixStages"
                :key="stage"
                class="flex items-center gap-2 rounded-md border px-3 py-2 text-sm"
                :class="index <= activeFixStageIndex ? 'border-primary/50 bg-primary/5 text-foreground' : 'text-muted-foreground'"
              >
                <span class="grid size-5 shrink-0 place-items-center rounded-full border text-xs tabular-nums">
                  {{ index + 1 }}
                </span>
                {{ $t(fixStageLabels[stage]) }}
              </li>
            </ol>
            <div class="flex flex-wrap items-center gap-2 text-sm">
              <Badge v-if="state.defense.state" variant="outline">
                {{ gameplayFactStateLabel(state.defense.state) }}
              </Badge>
              <span v-if="state.defense.result">
                {{ $t('评测结果:') }}<strong>{{ gameplayFactResultLabel(state.defense.result) }}</strong>
              </span>
              <span v-if="state.defense.failureCode" class="text-muted-foreground">
                {{ $t('失败原因') }}：{{ gameplayFactFailureCodeLabel(state.defense.failureCode) }}
              </span>
            </div>
          </CardContent>
        </Card>

        <FixSubmit
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          @accepted="refreshAndPoll"
          @evaluated="refreshAndPoll"
        />
      </section>
    </div>
  </div>
</template>
