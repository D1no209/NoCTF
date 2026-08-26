<script setup lang="ts">
import { ChevronRight, Flag, ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol,
} from '~/api'
import { bloodRankLabel } from '~/components/leaderboard/types'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardCurrentChallengeScore, scoreboardSlot } from '~/utils/scoreboard'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const props = defineProps<{
  competitionId: string
  selectedChallengeId?: string | null
}>()

const emit = defineEmits<{
  ready: [challengeId: string | null]
  select: [challengeId: string]
}>()

const ctx = inject(competitionContextKey)!
const { isLoggedIn } = useAuth()
const isAwdp = computed(() => ctx.competition.value?.mode === 'Awdp')
const items = ref<Challenge[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const dataScope = ref<NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol>('Live')
const myTeamId = ref<string | null>(null)
const board = useScoreboardMatrix(props.competitionId)

async function loadMyTeam(): Promise<void> {
  if (!isLoggedIn.value) {
    myTeamId.value = null
    return
  }
  const { data, error: requestError } = await getMyTeamEndpoint({
    path: { competitionId: props.competitionId },
  })
  myTeamId.value = requestError ? null : data?.id ?? null
}

onMounted(async () => {
  const [{ data, error: requestError }] = await Promise.all([
    listChallengesEndpoint({ path: { competitionId: props.competitionId } }),
    loadMyTeam(),
  ])
  loading.value = false
  if (requestError || !data) {
    error.value = parseApiError(requestError, translate('加载题目失败')).message
    emit('ready', null)
    return
  }
  items.value = (data.items ?? []).filter(challenge => challenge.isPublished)
  dataScope.value = data.dataScope ?? 'Live'
  const selectedExists = items.value.some(item => item.id === props.selectedChallengeId)
  emit('ready', selectedExists ? props.selectedChallengeId ?? null : items.value[0]?.id ?? null)
})

watch(isLoggedIn, () => void loadMyTeam())

watch(
  () => props.selectedChallengeId,
  (selectedChallengeId) => {
    if (loading.value || !items.value.length) return
    if (!selectedChallengeId || !items.value.some(item => item.id === selectedChallengeId))
      emit('ready', items.value[0]?.id ?? null)
  },
)

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

function currentScore(challengeId?: string): number | null {
  return scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)
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
</script>

<template>
  <aside
    class="min-h-0 border-y bg-background/30 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)]"
    :aria-label="$t('题目列表')"
  >
    <header class="border-b px-4 py-3">
      <h2 class="text-sm font-semibold">{{ $t('题目列表') }}</h2>
      <p class="mt-1 text-xs text-muted-foreground">{{ $t('按方向选择题目并在中间查看详情') }}</p>
    </header>

    <div v-if="loading" class="flex flex-col gap-2 p-3">
      <Skeleton v-for="index in 7" :key="index" class="h-12 w-full" />
    </div>

    <div v-else-if="error" class="p-3">
      <Alert variant="destructive">
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>
    </div>

    <Empty v-else-if="!items.length" class="border-0 py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('暂无已发布的题目') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>

    <div v-else class="flex max-h-[calc(100svh-12rem)] flex-col gap-4 overflow-y-auto p-3">
      <Alert v-if="board.error.value" variant="destructive" class="text-xs">
        <AlertDescription>{{ board.error.value }}</AlertDescription>
      </Alert>
      <Alert v-else-if="board.processing.value" class="text-xs">
        <AlertDescription class="flex items-center gap-2">
          <Spinner class="size-3" /> {{ $t('记分板数据投影中,请稍候…') }}
        </AlertDescription>
      </Alert>
      <Alert v-else-if="dataScope === 'Frozen'" class="text-xs">
        <AlertDescription>{{ $t('排行榜已冻结,题目分数显示为冻结时快照。') }}</AlertDescription>
      </Alert>

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
          @click="emit('select', challenge.id!)"
        >
          <span class="min-w-0 flex-1">
            <span class="flex min-w-0 items-baseline gap-2 text-sm font-medium">
              <span class="truncate">{{ challenge.title }}</span>
              <span v-if="currentScore(challenge.id) !== null" class="shrink-0 font-mono text-xs tabular-nums text-primary">
                {{ currentScore(challenge.id) }} pts
              </span>
            </span>
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
</template>
