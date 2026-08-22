<script setup lang="ts">
import { ChevronRight, Flag, ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol,
} from '~/api'
import { competitionWorkspaceNavigationKey } from '~/components/app/workspace-nav'
import { bloodRankLabel } from '~/components/leaderboard/types'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardSlot } from '~/utils/scoreboard'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const route = useRoute()
const router = useRouter()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const workspaceNavGroups = inject(competitionWorkspaceNavigationKey, computed(() => []))
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
  const { data, error: requestError } = await getMyTeamEndpoint({ path: { competitionId } })
  myTeamId.value = requestError ? null : data?.id ?? null
}

onMounted(async () => {
  const [{ data, error: requestError }] = await Promise.all([
    listChallengesEndpoint({ path: { competitionId } }),
    loadMyTeam(),
  ])
  loading.value = false
  if (requestError || !data) {
    error.value = parseApiError(requestError, translate('加载题目失败')).message
    return
  }
  items.value = (data.items ?? []).filter(challenge => challenge.isPublished)
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
  if (board.snapshot.value?.dataScope === 'Hidden') return progress

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
  if (!board.snapshot.value || board.snapshot.value.dataScope === 'Hidden') return null
  return progressByChallenge.value.get(challengeId ?? '') ?? {
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
  const grouped = new Map<string, Challenge[]>()
  for (const item of items.value) {
    const direction = item.direction || translate('未分类')
    const challenges = grouped.get(direction) ?? []
    challenges.push(item)
    grouped.set(direction, challenges)
  }
  return [...grouped.entries()].map(([direction, challenges]) => ({
    direction,
    challenges: challenges.sort((left, right) => (left.order ?? 0) - (right.order ?? 0)),
  }))
})

const requestedChallengeId = computed(() => typeof route.query.challenge === 'string' ? route.query.challenge : null)
const selectedChallengeId = computed(() => {
  const requested = requestedChallengeId.value
  if (requested && items.value.some(item => item.id === requested)) return requested
  return items.value[0]?.id ?? null
})

async function selectChallenge(challengeId: string): Promise<void> {
  if (selectedChallengeId.value === challengeId && requestedChallengeId.value === challengeId) return
  await router.replace({
    path: `/competitions/${competitionId}/challenges`,
    query: { ...route.query, challenge: challengeId },
  })
}
</script>

<template>
  <div class="flex flex-col gap-3">
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

    <div
      class="grid min-h-[calc(100svh-12rem)] items-stretch gap-4 xl:grid-cols-[15rem_minmax(0,1fr)_19rem]"
    >
      <aside class="min-h-0 border-y bg-background/30 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)]" :aria-label="$t('题目列表')">
        <header class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold">{{ $t('题目列表') }}</h2>
          <p class="mt-1 text-xs text-muted-foreground">{{ $t('按方向选择题目并在中间查看详情') }}</p>
        </header>

        <div v-if="loading" class="flex flex-col gap-2 p-3">
          <Skeleton v-for="index in 7" :key="index" class="h-12 w-full" />
        </div>

        <Empty v-else-if="!items.length" class="border-0 py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无已发布的题目') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>

        <div v-else class="flex max-h-[calc(100svh-12rem)] flex-col gap-4 overflow-y-auto p-3">
          <section v-for="group in groups" :key="group.direction" class="flex flex-col gap-2">
            <h3 class="flex items-center gap-2 px-1 text-xs font-semibold">
              <component
                :is="directionIcon(group.direction)"
                class="size-4"
                :class="directionTextClass(group.direction)"
                aria-hidden="true"
              />
              <span class="truncate">{{ group.direction }}</span>
              <span class="ml-auto font-mono text-[0.6875rem] tabular-nums text-muted-foreground">{{ group.challenges.length }}</span>
            </h3>

            <button
              v-for="challenge in group.challenges"
              :key="challenge.id"
              type="button"
              class="group flex w-full items-center gap-2 rounded-md border px-3 py-2.5 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              :class="selectedChallengeId === challenge.id
                ? 'border-primary bg-primary/10 text-foreground'
                : 'border-border bg-background text-muted-foreground hover:border-primary/40 hover:text-foreground'"
              :aria-current="selectedChallengeId === challenge.id ? 'true' : undefined"
              @click="selectChallenge(challenge.id!)"
            >
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium">{{ challenge.title }}</span>
                <span v-if="isAwdp && progressFor(challenge.id)" class="mt-1 flex items-center gap-2 text-[0.6875rem]">
                  <span class="flex items-center gap-1"><Swords class="size-3" /><span class="font-mono">{{ progressFor(challenge.id)?.attackCount ?? 0 }}</span></span>
                  <span class="flex items-center gap-1"><ShieldCheck class="size-3" /><span class="font-mono">{{ progressFor(challenge.id)?.defenseCount ?? 0 }}</span></span>
                  <span v-if="board.snapshot.value?.currentRoundId" class="truncate text-muted-foreground">{{ $t('本轮待结算') }}</span>
                </span>
                <span v-else-if="progressFor(challenge.id)" class="mt-1 flex items-center gap-1 text-[0.6875rem]">
                  <Users class="size-3" />
                  <span>{{ $t('{count} 支队伍已解出', { count: progressFor(challenge.id)?.solveCount ?? 0 }) }}</span>
                </span>
              </span>
              <Flag
                v-if="progressFor(challenge.id)?.solvedByMyTeam"
                class="size-4 shrink-0 text-primary"
                :aria-label="progressFor(challenge.id)?.bloodRank
                  ? bloodRankLabel(progressFor(challenge.id)?.bloodRank)
                  : $t('已解出')"
              />
              <component
                :is="progressFor(challenge.id)?.attackSucceeded ? Swords : ShieldCheck"
                v-else-if="isAwdp && awdpProgressLabel(progressFor(challenge.id))"
                class="size-4 shrink-0 text-primary"
                :aria-label="awdpProgressLabel(progressFor(challenge.id)) ?? undefined"
              />
              <ChevronRight class="size-4 shrink-0 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </button>
          </section>
        </div>
      </aside>

      <main class="min-w-0 border-y px-1 py-4 md:px-3 md:py-5">
        <CompetitionChallengeDetail
          v-if="selectedChallengeId"
          :competition-id="competitionId"
          :competition-challenge-id="selectedChallengeId"
        />
        <Empty v-else class="h-full min-h-80 border-0">
          <EmptyHeader>
            <EmptyTitle>{{ $t('选择题目查看详情') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </main>

      <aside class="grid min-h-0 content-start gap-4 sm:grid-cols-2 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)] xl:grid-cols-1 xl:grid-rows-[auto_minmax(0,1fr)]">
        <CompetitionWorkspaceNavigation :groups="workspaceNavGroups" />
        <CompetitionBroadcastPanel class="min-h-0 xl:static xl:flex xl:h-full xl:flex-col" :competition-id="competitionId" fill />
      </aside>
    </div>
  </div>
</template>
