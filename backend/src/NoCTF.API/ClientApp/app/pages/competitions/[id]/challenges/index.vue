<script setup lang="ts">
import { Flag, ShieldCheck, Swords, Users } from '@lucide/vue'
import { getLeaderboardEndpoint, getMyTeamEndpoint, listChallengesEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '~/api'
import { bloodRankLabel, normalizeChallengeKey } from '~/components/leaderboard/types'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const { isLoggedIn } = useAuth()
const isAwdp = computed(() => ctx.competition.value?.mode === 'Awdp')

const items = ref<Challenge[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const leaderboardError = ref<string | null>(null)
const leaderboardPending = ref(false)
const dataScope = ref<NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol>('Live')
const leaderboard = ref<NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null>(null)
const myTeamId = ref<string | null>(null)

async function loadLeaderboard(): Promise<boolean> {
  const { data, error: err, response } = await getLeaderboardEndpoint({
    path: { competitionId },
  })
  if (response?.status === 202) {
    leaderboardPending.value = true
    leaderboardError.value = null
    return false
  }
  if (err || !data) {
    leaderboardPending.value = false
    leaderboardError.value = parseApiError(err, translate("加载记分板失败")).message
    return true
  }
  leaderboardPending.value = false
  leaderboardError.value = null
  leaderboard.value = data as NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse
  return true
}

async function loadMyTeam(): Promise<void> {
  if (!isLoggedIn.value) {
    myTeamId.value = null
    return
  }
  const { data, error: err } = await getMyTeamEndpoint({ path: { competitionId } })
  myTeamId.value = err ? null : data?.id ?? null
}

const { start: startLeaderboardPolling, stop: stopLeaderboardPolling } = usePolling(
  loadLeaderboard,
  { interval: 2000, timeout: 60_000 },
)

onMounted(async () => {
  const [{ data, error: err }, leaderboardReady] = await Promise.all([
    listChallengesEndpoint({ path: { competitionId } }),
    loadLeaderboard(),
    loadMyTeam(),
  ])
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("加载题目失败")).message
    return
  }
  items.value = (data.items ?? []).filter((c) => c.isPublished)
  dataScope.value = data.dataScope ?? 'Live'
  if (!leaderboardReady) startLeaderboardPolling()
})

watch(isLoggedIn, () => void loadMyTeam())

let unwatchCompetition: (() => void) | null = null
onMounted(() => {
  unwatchCompetition = watchCompetition(competitionId, {
    leaderboardRefreshed: () => void loadLeaderboard(),
  })
})
onBeforeUnmount(() => {
  unwatchCompetition?.()
  unwatchCompetition = null
  stopLeaderboardPolling()
})

interface ChallengeProgress {
  solveCount: number
  solvedByMyTeam: boolean
  myScore: number | null
  bloodRank: string | null
  attackCount: number
  defenseCount: number
  attackSucceeded: boolean
  defenseSucceeded: boolean
  attackScore: number
  defenseScore: number
}

const progressByChallenge = computed(() => {
  const progress = new Map<string, ChallengeProgress>()
  if (leaderboard.value?.dataScope === 'Hidden')
    return progress

  for (const entry of leaderboard.value?.entries ?? []) {
    for (const cell of entry.cells ?? []) {
      const key = normalizeChallengeKey(cell.competitionChallengeId)
      if (!key) continue
      const current = progress.get(key) ?? {
        solveCount: 0,
        solvedByMyTeam: false,
        myScore: null,
        bloodRank: null,
        attackCount: 0,
        defenseCount: 0,
        attackSucceeded: false,
        defenseSucceeded: false,
        attackScore: 0,
        defenseScore: 0,
      }
      const attackScore = cell.attackScore ?? 0
      const defenseScore = cell.defenseScore ?? 0
      if (isAwdp.value) {
        if (attackScore > 0) current.attackCount += 1
        if (defenseScore > 0) current.defenseCount += 1
      }
      else {
        current.solveCount += 1
      }
      if (entry.teamId && entry.teamId === myTeamId.value) {
        current.attackSucceeded = attackScore > 0
        current.defenseSucceeded = defenseScore > 0
        current.solvedByMyTeam = isAwdp.value
          ? current.attackSucceeded && current.defenseSucceeded
          : true
        current.myScore = cell.score ?? null
        current.bloodRank = cell.bloodRank ?? null
        current.attackScore = attackScore
        current.defenseScore = defenseScore
      }
      progress.set(key, current)
    }
  }
  return progress
})

function progressFor(challengeId?: string): ChallengeProgress | null {
  if (!leaderboard.value || leaderboard.value.dataScope === 'Hidden')
    return null
  return progressByChallenge.value.get(normalizeChallengeKey(challengeId))
    ?? {
      solveCount: 0,
      solvedByMyTeam: false,
      myScore: null,
      bloodRank: null,
      attackCount: 0,
      defenseCount: 0,
      attackSucceeded: false,
      defenseSucceeded: false,
      attackScore: 0,
      defenseScore: 0,
    }
}

function awdpProgressLabel(progress: ChallengeProgress | null): string | null {
  if (!progress) return null
  if (progress.attackSucceeded && progress.defenseSucceeded) return translate('已解出')
  if (progress.attackSucceeded) return translate('攻击成功')
  if (progress.defenseSucceeded) return translate('防御成功')
  return null
}

const scoreInfoByChallenge = computed(() => new Map(
  (leaderboard.value?.challenges ?? []).map(challenge => [
    normalizeChallengeKey(challenge.competitionChallengeId),
    challenge,
  ]),
))

function currentScoreFor(challenge: Challenge): number | null {
  if (isAwdp.value) return null
  if (!leaderboard.value) return null
  if (leaderboard.value.dataScope === 'Hidden')
    return challenge.baseScore ?? null
  const info = scoreInfoByChallenge.value.get(normalizeChallengeKey(challenge.id))
  return info?.currentScore ?? challenge.baseScore ?? null
}

function awdpScoreFor(challenge: Challenge, kind: 'Break' | 'Fix'): number | null {
  if (!isAwdp.value || !leaderboard.value || leaderboard.value.dataScope === 'Hidden') return null
  const info = scoreInfoByChallenge.value.get(normalizeChallengeKey(challenge.id))
  return kind === 'Break' ? info?.currentBreakScore ?? null : info?.currentFixScore ?? null
}

const groups = computed(() => {
  const map = new Map<string, Challenge[]>()
  for (const item of items.value) {
    const direction = item.direction || translate('未分类')
    const list = map.get(direction) ?? []
    list.push(item)
    map.set(direction, list)
  }
  return [...map.entries()].map(([direction, challenges]) => ({
    direction,
    challenges: challenges.sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
  }))
})
</script>

<template>
  <div class="flex flex-col gap-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-if="leaderboardError" variant="destructive">
      <AlertDescription>{{ leaderboardError }}</AlertDescription>
    </Alert>
    <Alert v-else-if="leaderboardPending">
      <AlertDescription class="flex items-center gap-2">
        <Spinner class="size-3" /> {{ $t('记分板数据投影中,请稍候…') }}
      </AlertDescription>
    </Alert>
    <Alert v-else-if="dataScope === 'Frozen'">
      <AlertDescription>{{ $t('排行榜已冻结,题目分数显示为冻结时快照。') }}</AlertDescription>
    </Alert>

    <div class="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_18rem]">
      <main class="min-w-0">
        <div v-if="loading" class="grid gap-5 sm:grid-cols-2">
          <Skeleton v-for="i in 6" :key="i" class="h-28 w-full" />
        </div>

        <Empty v-else-if="!items.length" class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无已发布的题目') }}</EmptyTitle>
            <EmptyDescription>{{ $t('题目开放后会同步显示在右侧赛事播报中') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>

        <div v-else class="flex flex-col gap-8">
          <section v-for="group in groups" :key="group.direction" class="flex flex-col gap-4">
            <h2 class="flex items-center gap-2.5 text-display text-lg">
              <component
                :is="directionIcon(group.direction)"
                class="size-5"
                :class="directionTextClass(group.direction)"
              />
              {{ group.direction }}
              <Badge variant="secondary" class="font-mono tabular-nums">{{ group.challenges.length }}</Badge>
            </h2>
            <div class="grid gap-5 sm:grid-cols-2">
              <NuxtLink
                v-for="challenge in group.challenges"
                :key="challenge.id"
                :to="`/competitions/${competitionId}/challenges/${challenge.id}`"
                class="group block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <Card
                  class="relative h-full overflow-hidden transition-[transform,border-color,box-shadow,background-color] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] group-hover:-translate-y-0.5 group-hover:border-primary/50 group-hover:shadow-lg"
                  :class="progressFor(challenge.id)?.solvedByMyTeam || progressFor(challenge.id)?.attackSucceeded || progressFor(challenge.id)?.defenseSucceeded ? 'border-primary/50 bg-primary/5' : ''"
                >
                  <Flag
                    v-if="progressFor(challenge.id)?.solvedByMyTeam"
                    aria-hidden="true"
                    class="pointer-events-none absolute -right-2 -bottom-2 size-20 -rotate-12 text-primary/10"
                  />
                  <CardHeader>
                    <div class="flex items-start justify-between gap-2">
                      <CardTitle class="text-base leading-snug group-hover:text-primary">
                        {{ challenge.title }}
                      </CardTitle>
                      <Badge variant="outline" :class="directionBadgeClass(challenge.direction)">
                        {{ challenge.direction }}
                      </Badge>
                    </div>
                  </CardHeader>
                  <CardContent class="relative flex items-end justify-between gap-3">
                    <div class="flex flex-col items-start gap-2">
                      <Badge v-if="!leaderboard && leaderboardError" variant="destructive"> {{ $t('加载记分板失败') }} </Badge>
                      <Badge v-else-if="!leaderboard && leaderboardPending" variant="secondary"> {{ $t('记分板数据投影中,请稍候…') }} </Badge>
                      <div v-else-if="isAwdp && (awdpScoreFor(challenge, 'Break') !== null || awdpScoreFor(challenge, 'Fix') !== null)" class="flex flex-wrap gap-x-4 gap-y-1">
                        <span class="font-mono text-sm font-semibold text-primary tabular-nums">
                          Break {{ awdpScoreFor(challenge, 'Break') ?? '-' }} pts
                        </span>
                        <span class="font-mono text-sm font-semibold text-primary tabular-nums">
                          Fix {{ awdpScoreFor(challenge, 'Fix') ?? '-' }} pts
                        </span>
                      </div>
                      <Badge v-else-if="isAwdp || currentScoreFor(challenge) === null" variant="secondary"> {{ $t('分数隐藏') }} </Badge>
                      <span v-else class="font-mono text-lg font-bold text-primary tabular-nums">
                        {{ currentScoreFor(challenge) }}<span class="ml-1 text-xs font-medium text-muted-foreground">pts</span>
                      </span>
                      <span v-if="isAwdp && (awdpScoreFor(challenge, 'Break') !== null || awdpScoreFor(challenge, 'Fix') !== null)" class="text-xs text-muted-foreground">{{ $t('本轮参考分值（轮末结算）') }}</span>
                      <span v-else-if="currentScoreFor(challenge) !== null" class="text-xs text-muted-foreground">{{ $t('当前动态分值') }}</span>
                      <Badge v-if="isAwdp && awdpProgressLabel(progressFor(challenge.id))" variant="secondary" class="gap-1">
                        <Flag v-if="progressFor(challenge.id)?.solvedByMyTeam" class="size-3" />
                        <Swords v-else-if="progressFor(challenge.id)?.attackSucceeded" class="size-3" />
                        <ShieldCheck v-else class="size-3" />
                        {{ awdpProgressLabel(progressFor(challenge.id)) }}
                      </Badge>
                      <Badge v-else-if="!isAwdp && progressFor(challenge.id)?.solvedByMyTeam" variant="secondary" class="gap-1">
                        <Flag class="size-3" />
                        {{ progressFor(challenge.id)?.bloodRank
                          ? bloodRankLabel(progressFor(challenge.id)?.bloodRank)
                          : $t('已解出') }}
                      </Badge>
                      <span
                        v-if="progressFor(challenge.id)?.myScore !== null"
                        class="font-mono text-xs text-muted-foreground tabular-nums"
                      >
                        {{ $t('本队结算 {score} pts', { score: progressFor(challenge.id)?.myScore ?? 0 }) }}
                      </span>
                    </div>
                    <span
                      v-if="isAwdp && progressFor(challenge.id)"
                      class="flex items-center gap-2 text-xs text-muted-foreground"
                      :aria-label="$t('攻击成功 {attack} 支，防御成功 {defense} 支', { attack: progressFor(challenge.id)?.attackCount ?? 0, defense: progressFor(challenge.id)?.defenseCount ?? 0 })"
                    >
                      <span class="flex items-center gap-1"><Swords class="size-3.5" aria-hidden="true" /><span class="font-mono tabular-nums">{{ progressFor(challenge.id)?.attackCount ?? 0 }}</span></span>
                      <span class="flex items-center gap-1"><ShieldCheck class="size-3.5" aria-hidden="true" /><span class="font-mono tabular-nums">{{ progressFor(challenge.id)?.defenseCount ?? 0 }}</span></span>
                    </span>
                    <span
                      v-else-if="progressFor(challenge.id)"
                      class="flex items-center gap-1 text-xs text-muted-foreground"
                      :aria-label="$t('{count} 支队伍已解出', { count: progressFor(challenge.id)?.solveCount ?? 0 })"
                    >
                      <Users class="size-3.5" aria-hidden="true" />
                      <span class="font-mono tabular-nums">{{ progressFor(challenge.id)?.solveCount ?? 0 }}</span>
                      <span>{{ $t('解出') }}</span>
                    </span>
                  </CardContent>
                </Card>
              </NuxtLink>
            </div>
          </section>
        </div>
      </main>

      <CompetitionBroadcastPanel :competition-id="competitionId" />
    </div>
  </div>
</template>
