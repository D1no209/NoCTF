<script setup lang="ts">
import { Flag, ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol,
} from '~/api'
import { bloodRankLabel } from '~/components/leaderboard/types'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardSlot } from '~/utils/scoreboard'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const { isLoggedIn } = useAuth()
const isAwdp = computed(() => ctx.competition.value?.mode === 'Awdp')

const items = ref<Challenge[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const dataScope = ref<NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol>('Live')
const myTeamId = ref<string | null>(null)
const board = useScoreboardMatrix(competitionId)

async function loadMyTeam(): Promise<void> {
  if (!isLoggedIn.value) {
    myTeamId.value = null
    return
  }
  const { data, error: err } = await getMyTeamEndpoint({ path: { competitionId } })
  myTeamId.value = err ? null : data?.id ?? null
}

onMounted(async () => {
  const [{ data, error: err }] = await Promise.all([
    listChallengesEndpoint({ path: { competitionId } }),
    loadMyTeam(),
  ])
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("加载题目失败")).message
    return
  }
  items.value = (data.items ?? []).filter((c) => c.isPublished)
  dataScope.value = data.dataScope ?? 'Live'
})

watch(isLoggedIn, () => void loadMyTeam())

interface ChallengeProgress {
  solveCount: number
  solvedByMyTeam: boolean
  bloodRank: string | null
  attackCount: number
  defenseCount: number
  attackSucceeded: boolean
  defenseSucceeded: boolean
}

const progressByChallenge = computed(() => {
  const progress = new Map<string, ChallengeProgress>()
  if (board.snapshot.value?.dataScope === 'Hidden')
    return progress

  for (const challenge of board.catalog.value?.items ?? []) {
    if (!challenge.id) continue
    const columns = scoreboardColumnsForChallenge(board.schema.value, challenge.id)
    const current: ChallengeProgress = {
      solveCount: 0,
      solvedByMyTeam: false,
      bloodRank: null,
      attackCount: 0,
      defenseCount: 0,
      attackSucceeded: false,
      defenseSucceeded: false,
    }
    for (const team of board.snapshot.value?.teams ?? []) {
      let teamSolved = false
      let teamAttack = false
      let teamDefense = false
      for (const column of columns) {
        if (column.index === undefined) continue
        const slot = scoreboardSlot(team, column.index)
        if (!slot) continue
        teamSolved ||= (scoreboardBreakdown(slot, 'Solve')?.successfulCount ?? 0) > 0
        teamAttack ||= (scoreboardBreakdown(slot, 'Attack')?.successfulCount ?? 0) > 0
        teamDefense ||= (scoreboardBreakdown(slot, 'Defense')?.successfulCount ?? 0) > 0
        if (team.teamId === myTeamId.value && !current.bloodRank) {
          const award = slot.entries?.find(entry => entry.award)?.award
          current.bloodRank = award === 'FirstBlood' ? 'First'
            : award === 'SecondBlood' ? 'Second'
              : award === 'ThirdBlood' ? 'Third' : null
        }
      }
      if (teamSolved) current.solveCount += 1
      if (teamAttack) current.attackCount += 1
      if (teamDefense) current.defenseCount += 1
      if (team.teamId === myTeamId.value) {
        current.attackSucceeded = teamAttack
        current.defenseSucceeded = teamDefense
        current.solvedByMyTeam = isAwdp.value ? teamAttack && teamDefense : teamSolved
      }
    }
    progress.set(challenge.id, current)
  }
  return progress
})

function progressFor(challengeId?: string): ChallengeProgress | null {
  if (!board.snapshot.value || board.snapshot.value.dataScope === 'Hidden')
    return null
  return progressByChallenge.value.get(challengeId ?? '')
    ?? {
      solveCount: 0,
      solvedByMyTeam: false,
      bloodRank: null,
      attackCount: 0,
      defenseCount: 0,
      attackSucceeded: false,
      defenseSucceeded: false,
    }
}

function awdpProgressLabel(progress: ChallengeProgress | null): string | null {
  if (!progress) return null
  if (progress.attackSucceeded && progress.defenseSucceeded) return translate('已解出')
  if (progress.attackSucceeded) return translate('攻击成功')
  if (progress.defenseSucceeded) return translate('防御成功')
  return null
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
    <Alert v-if="board.error.value" variant="destructive">
      <AlertDescription>{{ board.error.value }}</AlertDescription>
    </Alert>
    <Alert v-else-if="board.processing.value">
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
                      <Badge v-if="!board.snapshot.value && board.error.value" variant="destructive"> {{ $t('加载记分板失败') }} </Badge>
                      <Badge v-else-if="!board.snapshot.value && board.processing.value" variant="secondary"> {{ $t('记分板数据投影中,请稍候…') }} </Badge>
                      <Badge v-else-if="isAwdp && board.snapshot.value?.currentRoundId" variant="secondary">{{ $t('本轮待结算') }}</Badge>
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
