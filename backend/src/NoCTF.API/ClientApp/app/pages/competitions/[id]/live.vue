<script setup lang="ts">
import {
  Clock3,
  Expand,
  Minimize,
  Radio,
  RefreshCw,
  ShieldCheck,
  Trophy,
  Users,
  X,
} from '@lucide/vue'
import { getCompetitionEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'
import {
  controlScreenChallenges,
  controlScreenPublicEntries,
  controlScreenSolveFeed,
  reconcileControlScreenSolves,
} from '~/utils/control-screen'
import type { ControlScreenBloodRank, ControlScreenSolve } from '~/utils/control-screen'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'
import { LiveCityScene } from '~/lib/live-city-3d'
import type { LiveCityBlood, LiveCityChallengeState } from '~/lib/live-city-3d'
import { scoreboardRankingStateLabel, scoreboardTeamSolveCount } from '~/utils/scoreboard'

definePageMeta({ layout: false })

const route = useRoute()
const competitionId = route.params.id as string
const { configuration, ensureLoaded } = usePlatform()
const { t } = useLocale()
const board = useScoreboardMatrix(competitionId)

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const loading = ref(true)
const refreshing = ref(false)
const projectionPending = ref(false)
const error = ref<string | null>(null)
const featuredSolve = ref<ControlScreenSolve | null>(null)
const celebrationQueue = ref<ControlScreenSolve[]>([])
const fullscreen = ref(false)
const clock = ref(Date.now())
const arenaRef = ref<HTMLElement | null>(null)
let scene: LiveCityScene | null = null
let clockTimer: ReturnType<typeof setInterval> | undefined
let refreshTimer: ReturnType<typeof setInterval> | undefined
let projectionTimer: ReturnType<typeof setTimeout> | undefined
let celebrationTimer: ReturnType<typeof setTimeout> | undefined
let seenSolveKeys: Set<string> | null = null
let unwatch: (() => void) | undefined

const entries = computed(() => controlScreenPublicEntries(board.snapshot.value))
const rankedEntries = computed(() => entries.value)
const challenges = computed(() => controlScreenChallenges(board.catalog.value, board.schema.value, entries.value))
const solveFeed = computed(() => controlScreenSolveFeed(board.catalog.value, board.schema.value, entries.value).slice(0, 10))
const solvedChallengeCount = computed(() => challenges.value.filter(challenge => challenge.solveCount > 0).length)
const totalSolveCount = computed(() => challenges.value.reduce((sum, challenge) => sum + challenge.solveCount, 0))
const dataAsOf = computed(() => board.snapshot.value?.dataAsOf ?? board.snapshot.value?.generatedAt)
const marqueeEnabled = computed(() => rankedEntries.value.length > 7)
const marqueeDuration = computed(() => Math.max(14, rankedEntries.value.length * 2.2))
const bloodToneOrder: Record<LiveCityBlood['tone'], number> = { first: 0, second: 1, third: 2 }

const celebrationParticles = Array.from({ length: 30 }, (_, index) => ({
  id: index,
  angle: `${index * (360 / 30)}deg`,
  distance: `${10 + (index % 5) * 2.1}rem`,
  delay: `${(index % 7) * 30}ms`,
}))

const remainingText = computed(() => {
  const end = competition.value?.endTime ? new Date(competition.value.endTime).getTime() : null
  if (!end || competition.value?.status === 'Finished' || end <= clock.value) return t('比赛已结束')
  const total = Math.max(0, Math.floor((end - clock.value) / 1000))
  const days = Math.floor(total / 86400)
  const hours = Math.floor(total % 86400 / 3600)
  const minutes = Math.floor(total % 3600 / 60)
  const seconds = total % 60
  return [days ? `${days}d` : '', `${hours.toString().padStart(2, '0')}h`, `${minutes.toString().padStart(2, '0')}m`, `${seconds.toString().padStart(2, '0')}s`]
    .filter(Boolean)
    .join(' ')
})

const elapsedText = computed(() => {
  const start = competition.value?.startTime ? new Date(competition.value.startTime).getTime() : null
  if (!start || clock.value <= start) return '00:00:00'
  const total = Math.floor((clock.value - start) / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor(total % 3600 / 60)
  const seconds = total % 60
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})

function bloodLabel(rank: ControlScreenBloodRank | null): string {
  if (rank === 'First') return t('一血')
  if (rank === 'Second') return t('二血')
  if (rank === 'Third') return t('三血')
  return t('攻克')
}

function bloodClass(rank: ControlScreenBloodRank | null): string {
  if (rank === 'First') return 'live-blood-first'
  if (rank === 'Second') return 'live-blood-second'
  if (rank === 'Third') return 'live-blood-third'
  return 'live-blood-solve'
}

function rankClass(rank?: number): string {
  if (rank === 1) return 'live-rank-first'
  if (rank === 2) return 'live-rank-second'
  if (rank === 3) return 'live-rank-third'
  return ''
}

function challengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

const bloodsByChallenge = computed(() => {
  const map = new Map<string, LiveCityBlood[]>()
  for (const solve of solveFeed.value) {
      if (!solve.bloodRank) continue
      const tone: LiveCityBlood['tone'] = solve.bloodRank === 'First'
        ? 'first'
        : solve.bloodRank === 'Second' ? 'second' : 'third'
      const key = challengeKey(solve.competitionChallengeId)
      const list = map.get(key) ?? []
      list.push({ label: bloodLabel(solve.bloodRank), teamName: solve.teamName, tone })
      map.set(key, list)
  }
  for (const list of map.values()) list.sort((left, right) => bloodToneOrder[left.tone] - bloodToneOrder[right.tone])
  return map
})

const cityStates = computed<LiveCityChallengeState[]>(() => challenges.value.map(challenge => ({
  id: challengeKey(challenge.competitionChallengeId),
  title: challenge.title,
  score: challenge.currentScore,
  solveCount: challenge.solveCount,
  solvesText: `${challenge.solveCount} ${t('解出')}`,
  solved: challenge.solveCount > 0,
  bloods: bloodsByChallenge.value.get(challengeKey(challenge.competitionChallengeId)) ?? [],
})))

watch(cityStates, (states) => {
  scene?.setChallenges(states)
}, { deep: true })

function scheduleCelebrationEnd(): void {
  if (celebrationTimer) clearTimeout(celebrationTimer)
  celebrationTimer = setTimeout(() => {
    celebrationTimer = undefined
    featuredSolve.value = null
    scene?.setLabelFocus(null)
    scene?.endFocus()
    celebrationTimer = setTimeout(() => {
      celebrationTimer = undefined
      playNextCelebration()
    }, 450)
  }, 5_200)
}

function focusSolve(solve: ControlScreenSolve): void {
  featuredSolve.value = solve
  const id = challengeKey(solve.competitionChallengeId)
  scene?.setLabelFocus(id)
  scene?.focus(id)
  scheduleCelebrationEnd()
}

function playNextCelebration(): void {
  if (featuredSolve.value || !celebrationQueue.value.length) return
  const next = celebrationQueue.value.shift()
  if (next) focusSolve(next)
}

function reconcileCelebrations(currentSolves: readonly ControlScreenSolve[]): void {
  const reconciled = reconcileControlScreenSolves(seenSolveKeys, currentSolves)
  seenSolveKeys = reconciled.seenKeys
  if (!reconciled.newSolves.length) return

  const activeChallengeId = challengeKey(featuredSolve.value?.competitionChallengeId)
  const sameChallengeUpdates = activeChallengeId
    ? reconciled.newSolves.filter(solve => challengeKey(solve.competitionChallengeId) === activeChallengeId)
    : []
  if (sameChallengeUpdates.length) focusSolve(sameChallengeUpdates.at(-1)!)

  celebrationQueue.value.push(...reconciled.newSolves.filter(
    solve => !activeChallengeId || challengeKey(solve.competitionChallengeId) !== activeChallengeId,
  ))
  playNextCelebration()
}

async function loadData(): Promise<void> {
  refreshing.value = Boolean(competition.value || board.snapshot.value)
  const competitionResult = await getCompetitionEndpoint({ path: { competitionId } })
  loading.value = false
  refreshing.value = false

  if (competitionResult.error || !competitionResult.data) {
    error.value = parseApiError(competitionResult.error, t('加载竞赛失败')).message
    return
  }
  competition.value = competitionResult.data
  if (competition.value.mode !== 'Ctf') {
    projectionPending.value = false
    error.value = t('3D 大屏当前仅支持 CTF 比赛')
    return
  }
  await board.refresh({ catalog: true, schema: true, snapshot: true })
  if (board.processing.value) {
    error.value = null
    projectionPending.value = true
    if (!projectionTimer) {
      projectionTimer = setTimeout(() => {
        projectionTimer = undefined
        void refreshLatest()
      }, 2_000)
    }
    return
  }
  if (board.error.value || !board.snapshot.value) {
    projectionPending.value = false
    error.value = board.error.value ?? t('加载记分板失败')
    return
  }
  projectionPending.value = false
  error.value = null
  await nextTick()
  reconcileCelebrations(controlScreenSolveFeed(board.catalog.value, board.schema.value, entries.value))
}

const refreshLatest = createTrailingRefresh(loadData)

async function toggleFullscreen(): Promise<void> {
  try {
    if (!document.fullscreenElement) await document.documentElement.requestFullscreen()
    else await document.exitFullscreen()
  }
  catch {
    // Some embedded browsers deny fullscreen; the screen remains fully usable.
  }
}

function syncFullscreen(): void {
  fullscreen.value = Boolean(document.fullscreenElement)
}

onMounted(async () => {
  if (arenaRef.value) scene = new LiveCityScene(arenaRef.value)
  scene?.setChallenges(cityStates.value)
  await ensureLoaded()
  await refreshLatest()
  clockTimer = setInterval(() => { clock.value = Date.now() }, 1000)
  refreshTimer = setInterval(() => void refreshLatest(), 15_000)
  document.addEventListener('fullscreenchange', syncFullscreen)
  unwatch = watchCompetition(competitionId, {
    scoreboardUpdated: () => void refreshLatest(),
    competitionLifecycleChanged: () => void refreshLatest(),
    onReconnected: () => void refreshLatest(),
  })
})

onUnmounted(() => {
  if (clockTimer) clearInterval(clockTimer)
  if (refreshTimer) clearInterval(refreshTimer)
  if (projectionTimer) clearTimeout(projectionTimer)
  if (celebrationTimer) clearTimeout(celebrationTimer)
  document.removeEventListener('fullscreenchange', syncFullscreen)
  unwatch?.()
  scene?.dispose()
  scene = null
})
</script>

<template>
  <div class="live-screen dark">
    <div class="live-screen-grid" aria-hidden="true" />
    <div class="live-screen-aurora" aria-hidden="true" />

    <header class="live-screen-header">
      <div class="min-w-0">
        <div class="flex items-center gap-3">
          <span class="live-screen-wordmark">{{ configuration?.name ?? 'NoCTF' }}</span>
          <span class="live-screen-divider" aria-hidden="true" />
          <h1 class="truncate text-xl font-semibold tracking-tight lg:text-2xl">{{ competition?.title ?? t('3D 大屏') }}</h1>
        </div>
        <p class="mt-1 font-mono text-[0.625rem] uppercase tracking-[0.24em] text-slate-400 lg:text-xs">
          {{ t('三维实时态势') }} · {{ t('全部公开赛道') }}
        </p>
      </div>

      <dl class="live-screen-metrics">
        <div>
          <dt>{{ t('已攻克') }}</dt>
          <dd>{{ solvedChallengeCount }}<span>/{{ challenges.length }}</span></dd>
        </div>
        <div>
          <dt>{{ t('解题记录') }}</dt>
          <dd>{{ totalSolveCount }}</dd>
        </div>
        <div>
          <dt>{{ t('参赛队伍') }}</dt>
          <dd>{{ entries.length }}</dd>
        </div>
      </dl>

      <div class="flex items-center gap-3">
        <div class="hidden text-right sm:block">
          <p class="text-[0.625rem] uppercase tracking-[0.2em] text-slate-500">{{ t('大屏距结束') }}</p>
          <p class="live-screen-countdown font-mono text-lg font-semibold tabular-nums lg:text-2xl">{{ remainingText }}</p>
        </div>
        <div class="hidden h-9 w-px bg-white/10 lg:block" />
        <div class="hidden text-right lg:block">
          <p class="text-[0.625rem] uppercase tracking-[0.2em] text-slate-500">{{ t('已进行') }}</p>
          <p class="font-mono text-sm tabular-nums text-slate-300">{{ elapsedText }}</p>
        </div>
        <Button variant="ghost" size="icon" class="text-slate-400 hover:bg-white/5 hover:text-white" :aria-label="t('刷新大屏')" @click="refreshLatest">
          <RefreshCw :class="['size-4', refreshing && 'animate-spin']" />
        </Button>
        <Button variant="ghost" size="icon" class="text-slate-400 hover:bg-white/5 hover:text-white" :aria-label="fullscreen ? t('退出全屏') : t('进入全屏')" @click="toggleFullscreen">
          <Minimize v-if="fullscreen" class="size-4" />
          <Expand v-else class="size-4" />
        </Button>
        <Button variant="ghost" size="icon" as-child class="text-slate-400 hover:bg-white/5 hover:text-white">
          <NuxtLink :to="`/competitions/${competitionId}/leaderboard`" :aria-label="t('退出 3D 大屏')">
            <X class="size-4" />
          </NuxtLink>
        </Button>
      </div>
    </header>

    <main class="live-screen-layout">
      <section
        ref="arenaRef"
        :class="['live-arena', featuredSolve && 'live-arena-focusing']"
        :aria-label="t('实时题目态势')"
      >
        <div class="live-arena-scanlines" aria-hidden="true" />
        <div class="live-arena-vignette" aria-hidden="true" />

        <div v-if="loading" class="live-overlay">
          <Radio class="live-overlay-icon size-9 animate-pulse" />
          <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('正在接入赛事数据') }}</p>
        </div>

        <div v-else-if="error" class="live-overlay">
          <ShieldCheck class="size-9 text-destructive" />
          <p class="max-w-lg text-center text-sm text-slate-300">{{ error }}</p>
          <Button variant="outline" class="border-white/15 bg-transparent text-white hover:bg-white/5" @click="refreshLatest">
            {{ t('重新加载') }}
          </Button>
        </div>

        <div v-else-if="projectionPending && !board.snapshot.value" class="live-overlay">
          <Radio class="live-overlay-icon size-9 animate-pulse" />
          <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('记分板正在生成') }}</p>
        </div>

        <div v-else-if="board.snapshot.value?.dataScope === 'Hidden'" class="live-overlay">
          <ShieldCheck class="live-overlay-icon size-9" />
          <p class="text-xl font-semibold">{{ t('排行榜暂不公开') }}</p>
          <p class="text-sm text-slate-400">{{ t('主办方当前隐藏了排行榜数据') }}</p>
        </div>

        <div class="live-arena-heading" aria-hidden="true">
          <div class="flex items-center gap-2">
            <span class="live-arena-pulse" />
            <span>{{ t('实时题目态势') }}</span>
          </div>
          <div v-if="board.snapshot.value?.dataScope === 'Frozen'" class="live-frozen">
            {{ t('冻结快照') }} · {{ formatDateTime(dataAsOf) }}
          </div>
          <div v-else class="font-mono text-[0.625rem] text-slate-500">{{ formatDateTime(dataAsOf) }}</div>
        </div>

        <Transition name="live-celebration" mode="out-in">
          <div v-if="featuredSolve" :key="featuredSolve.key" class="live-celebration" aria-live="assertive">
            <div class="live-celebration-flash" aria-hidden="true" />
            <div class="live-celebration-impact" aria-hidden="true">
              <i class="live-impact-ring live-impact-ring-one" />
              <i class="live-impact-ring live-impact-ring-two" />
              <i class="live-impact-ring live-impact-ring-three" />
              <span
                v-for="particle in celebrationParticles"
                :key="particle.id"
                class="live-celebration-particle"
                :style="{
                  '--particle-angle': particle.angle,
                  '--particle-distance': particle.distance,
                  '--particle-delay': particle.delay,
                }"
              />
            </div>
            <div class="live-celebration-card">
              <div class="live-celebration-eyebrow">
                <span>{{ bloodLabel(featuredSolve.bloodRank) }}</span>
                <i />
                <span>{{ t('解题确认') }}</span>
              </div>
              <strong>{{ featuredSolve.teamName }}</strong>
              <p>{{ t('攻克了') }} <b>{{ featuredSolve.challengeTitle }}</b></p>
              <div class="live-celebration-score">
                <span>+{{ featuredSolve.score }}</span>
                <small>PTS</small>
              </div>
              <div class="live-celebration-progress" aria-hidden="true"><i /></div>
            </div>
          </div>
        </Transition>

        <div class="live-corners" aria-hidden="true"><i /><i /><i /><i /></div>
      </section>

      <aside class="live-rail">
        <section class="live-panel min-h-0 flex-1">
          <header class="live-panel-heading">
            <div class="flex items-center gap-2"><Trophy class="size-4 text-primary" />{{ t('排行榜') }}</div>
            <span>{{ t('综合') }}</span>
          </header>
          <div v-if="rankedEntries.length" class="live-rank-viewport">
            <div
              :class="['live-rank-track', marqueeEnabled && 'live-rank-track-scroll']"
              :style="marqueeEnabled ? { animationDuration: `${marqueeDuration}s` } : undefined"
            >
              <ol class="live-ranking">
                <li v-for="entry in rankedEntries" :key="entry.teamId" :class="rankClass(entry.rank ?? undefined)">
                  <span class="live-rank">{{ entry.rank ?? '—' }}</span>
                  <span class="min-w-0 flex-1 truncate font-semibold">{{ entry.teamName }}<small v-if="entry.rankingState !== 'Eligible'"> · {{ scoreboardRankingStateLabel(entry.rankingState) }}</small></span>
                  <span class="font-mono text-[0.625rem] text-slate-500">{{ scoreboardTeamSolveCount(entry) }}</span>
                  <strong class="font-mono tabular-nums">{{ entry.totalScore ?? 0 }}</strong>
                </li>
              </ol>
              <ol v-if="marqueeEnabled" class="live-ranking" aria-hidden="true">
                <li v-for="entry in rankedEntries" :key="`clone-${entry.teamId}`" :class="rankClass(entry.rank ?? undefined)">
                  <span class="live-rank">{{ entry.rank ?? '—' }}</span>
                  <span class="min-w-0 flex-1 truncate font-semibold">{{ entry.teamName }}<small v-if="entry.rankingState !== 'Eligible'"> · {{ scoreboardRankingStateLabel(entry.rankingState) }}</small></span>
                  <span class="font-mono text-[0.625rem] text-slate-500">{{ scoreboardTeamSolveCount(entry) }}</span>
                  <strong class="font-mono tabular-nums">{{ entry.totalScore ?? 0 }}</strong>
                </li>
              </ol>
            </div>
          </div>
          <div v-else class="live-empty h-full">{{ t('还没有队伍得分') }}</div>
        </section>

        <section class="live-panel min-h-0 flex-1">
          <header class="live-panel-heading">
            <div class="flex items-center gap-2"><Radio class="size-4 text-primary" />{{ t('实时战报') }}</div>
            <span>LIVE</span>
          </header>
          <ol v-if="solveFeed.length" class="live-feed">
            <li v-for="solve in solveFeed" :key="solve.key">
              <time :datetime="solve.solvedAt">{{ new Date(solve.solvedAt).toLocaleTimeString(localeTag(), { hour12: false }) }}</time>
              <span :class="bloodClass(solve.bloodRank)">{{ bloodLabel(solve.bloodRank) }}</span>
              <p><strong>{{ solve.teamName }}</strong> {{ t('攻克了') }} <b>{{ solve.challengeTitle }}</b> <em>+{{ solve.score }}</em></p>
            </li>
          </ol>
          <div v-else class="live-empty h-full">{{ t('等待首个解题记录') }}</div>
        </section>
      </aside>
    </main>

    <footer class="live-screen-footer">
      <span class="flex items-center gap-2"><Radio class="size-3 text-primary" />{{ t('数据自动刷新') }}</span>
      <span class="hidden items-center gap-2 sm:flex"><Clock3 class="size-3" />{{ formatDateTime(dataAsOf) }}</span>
      <span class="ml-auto flex items-center gap-2"><Users class="size-3" />{{ entries.length }} {{ t('支队伍') }}</span>
    </footer>
  </div>
</template>

<style scoped>
.live-screen {
  --live-panel: rgba(12, 8, 25, .94);
  --live-line: rgba(158, 119, 237, .2);
  --live-green: oklch(0.78 0.22 149);
  --live-purple: oklch(0.67 0.19 300);
  --live-blue: #3ea6ff;
  --live-red: #ff3d5e;
  position: relative;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  width: 100%;
  min-height: 100svh;
  overflow: hidden;
  color: oklch(0.94 0.015 292);
  background: oklch(0.105 0.025 292);
  color-scheme: dark;
}
.live-screen-grid {
  position: absolute;
  inset: 0;
  pointer-events: none;
  opacity: .55;
  background-image:
    linear-gradient(rgba(158, 119, 237, .035) 1px, transparent 1px),
    linear-gradient(90deg, rgba(158, 119, 237, .035) 1px, transparent 1px);
  background-size: 42px 42px;
}
.live-screen-aurora {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background:
    radial-gradient(circle at 18% 12%, rgba(160, 107, 255, .1), transparent 34%),
    radial-gradient(circle at 84% 78%, rgba(45, 255, 143, .07), transparent 38%);
}
.live-screen-header,
.live-screen-footer {
  position: relative;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 1.5rem;
  border-color: rgba(148, 163, 184, .13);
  background: rgba(7, 5, 16, .95);
}
.live-screen-header { min-height: 5.5rem; border-bottom-width: 1px; padding: 1rem 1.5rem; }
.live-screen-footer { min-height: 2rem; border-top-width: 1px; padding: .4rem 1.5rem; font-size: .625rem; text-transform: uppercase; letter-spacing: .14em; color: #64748b; }
.live-screen-wordmark { font-family: var(--font-mono); font-weight: 700; letter-spacing: .18em; color: var(--live-purple); text-transform: uppercase; }
.live-screen-divider { width: 1px; height: 1.75rem; background: rgba(148, 163, 184, .18); }
.live-screen-metrics { margin-left: auto; display: grid; grid-auto-flow: column; gap: 1.75rem; }
.live-screen-metrics div { min-width: 4rem; text-align: center; }
.live-screen-metrics dt { font-size: .625rem; text-transform: uppercase; letter-spacing: .18em; color: #64748b; }
.live-screen-metrics dd { margin-top: .2rem; font-family: var(--font-mono); font-size: 1.4rem; font-weight: 700; color: #f8fafc; }
.live-screen-metrics dd span { margin-left: .15rem; font-size: .7rem; color: #64748b; }
.live-screen-countdown { color: var(--live-green); text-shadow: 0 0 18px rgba(45, 255, 143, .45); }

.live-screen-layout { position: relative; z-index: 1; display: grid; grid-template-columns: minmax(0, 1fr) clamp(18rem, 24vw, 27rem); min-height: 0; gap: .75rem; padding: .75rem; }
.live-arena {
  position: relative;
  min-height: 0;
  overflow: hidden;
  border: 1px solid var(--live-line);
  background: #070312;
  isolation: isolate;
}
.live-arena :deep(.live-city-canvas) { position: absolute; inset: 0; width: 100%; height: 100%; display: block; }
.live-arena :deep(.live-city-labels) { position: absolute; inset: 0; pointer-events: none; z-index: 3; }
.live-arena-scanlines {
  position: absolute;
  inset: 0;
  z-index: 4;
  pointer-events: none;
  opacity: .5;
  background: repeating-linear-gradient(180deg, rgba(255, 255, 255, .022) 0 1px, transparent 1px 4px);
}
.live-arena-vignette {
  position: absolute;
  inset: 0;
  z-index: 4;
  pointer-events: none;
  background: radial-gradient(ellipse at center, transparent 52%, rgba(4, 2, 10, .55) 100%);
}
.live-arena-heading {
  position: absolute;
  z-index: 5;
  inset: 1rem 1.25rem auto;
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  font-size: .7rem;
  font-weight: 700;
  letter-spacing: .16em;
  text-transform: uppercase;
  color: #94a3b8;
  pointer-events: none;
}
.live-arena-pulse { width: .5rem; height: .5rem; border-radius: 999px; background: var(--live-green); box-shadow: 0 0 18px rgba(45, 255, 143, .9); animation: live-dot-pulse 1.6s ease-in-out infinite; }
.live-frozen { color: #fbbf24; }
.live-overlay {
  position: absolute;
  inset: 0;
  z-index: 6;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  background: rgba(7, 3, 18, .82);
  color: #64748b;
}
.live-overlay-icon { color: var(--live-purple); }
.live-corners i { position: absolute; z-index: 5; width: 1.4rem; height: 1.4rem; border-color: rgba(160, 107, 255, .7); }
.live-corners i:nth-child(1) { left: .6rem; top: .6rem; border-left: 2px solid; border-top: 2px solid; }
.live-corners i:nth-child(2) { right: .6rem; top: .6rem; border-right: 2px solid; border-top: 2px solid; }
.live-corners i:nth-child(3) { left: .6rem; bottom: .6rem; border-left: 2px solid; border-bottom: 2px solid; }
.live-corners i:nth-child(4) { right: .6rem; bottom: .6rem; border-right: 2px solid; border-bottom: 2px solid; }

/* 3D 浮标(由 live-city-3d 动态创建,经 :deep 命中)。 */
.live-arena :deep(.live-label) {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: .18rem;
  min-width: 8.5rem;
  max-width: 14rem;
  padding: .42rem .7rem .48rem;
  border: 1px solid rgba(62, 166, 255, .5);
  background: linear-gradient(180deg, rgba(8, 6, 20, .94), rgba(8, 6, 20, .84));
  box-shadow: 0 0 16px rgba(62, 166, 255, .18), inset 0 0 12px rgba(62, 166, 255, .07);
  clip-path: polygon(0 0, calc(100% - .55rem) 0, 100% .55rem, 100% 100%, .55rem 100%, 0 calc(100% - .55rem));
  text-align: center;
  transition: opacity .5s ease, filter .5s ease, border-color .5s ease, box-shadow .5s ease;
}
.live-arena :deep(.live-label[data-state="solved"]) {
  border-color: rgba(255, 61, 94, .6);
  box-shadow: 0 0 16px rgba(255, 61, 94, .24), inset 0 0 12px rgba(255, 61, 94, .09);
}
.live-arena-focusing :deep(.live-label) { opacity: .16; filter: saturate(.4) blur(.4px); }
.live-arena-focusing :deep(.live-label[data-focus="true"]) { opacity: 1; filter: none; }
.live-arena :deep(.live-label-name) {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: .74rem;
  font-weight: 700;
  letter-spacing: .04em;
  color: #eaf2ff;
}
.live-arena :deep(.live-label-meta) { display: flex; align-items: baseline; gap: .5rem; }
.live-arena :deep(.live-label-pts) { font-family: var(--font-mono); font-size: .78rem; font-weight: 700; color: var(--live-blue); text-shadow: 0 0 10px rgba(62, 166, 255, .6); }
.live-arena :deep(.live-label[data-state="solved"] .live-label-pts) { color: var(--live-red); text-shadow: 0 0 10px rgba(255, 61, 94, .55); }
.live-arena :deep(.live-label-solves) { font-family: var(--font-mono); font-size: .62rem; color: #a9b5ca; }
.live-arena :deep(.live-label-bloods) { display: flex; flex-direction: column; gap: .12rem; margin-top: .08rem; }
.live-arena :deep(.live-label-blood) {
  max-width: 11rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  border: 1px solid currentColor;
  border-radius: 999px;
  padding: .04rem .42rem;
  font-size: .56rem;
  font-weight: 700;
}
.live-arena :deep(.live-label-blood-first) { color: #ffd166; text-shadow: 0 0 8px rgba(255, 209, 102, .6); }
.live-arena :deep(.live-label-blood-second) { color: #cbd5e1; }
.live-arena :deep(.live-label-blood-third) { color: #fb923c; }

/* 解题聚焦横幅。 */
.live-celebration { position: absolute; z-index: 9; inset: 0; display: grid; place-items: center; pointer-events: none; }
.live-celebration-flash { position: absolute; inset: 0; background: radial-gradient(circle at center, rgba(111, 255, 169, .3), transparent 48%); animation: live-flash 1.15s ease-out both; }
.live-celebration-impact { position: absolute; left: 50%; top: 59%; width: 1px; height: 1px; }
.live-impact-ring { position: absolute; left: 0; top: 0; width: 5rem; aspect-ratio: 1; border: 2px solid var(--live-green); border-radius: 50%; transform: translate(-50%, -50%) scale(.12); box-shadow: 0 0 18px rgba(36, 231, 118, .5); animation: live-impact-ring 1.5s cubic-bezier(.16, 1, .3, 1) both; }
.live-impact-ring-two { animation-delay: .18s; border-color: var(--live-purple); }
.live-impact-ring-three { animation-delay: .36s; }
.live-celebration-particle { --particle-angle: 0deg; --particle-distance: 12rem; --particle-delay: 0ms; position: absolute; left: 0; top: 0; width: .35rem; height: .35rem; background: var(--live-green); box-shadow: 0 0 12px rgba(38, 239, 126, .85); animation: live-particle 1.25s cubic-bezier(.16, 1, .3, 1) var(--particle-delay) both; }
.live-celebration-particle:nth-of-type(3n) { width: .24rem; height: .7rem; background: var(--live-purple); box-shadow: 0 0 12px rgba(170, 104, 236, .85); }
.live-celebration-card { position: relative; display: flex; min-width: min(31rem, 74%); flex-direction: column; align-items: center; border: 1px solid rgba(193, 153, 244, .55); background: rgba(9, 5, 19, .94); padding: 1.45rem 3rem 1.25rem; text-align: center; box-shadow: 0 0 0 1px rgba(41, 230, 120, .12), 0 0 58px rgba(93, 40, 153, .3); animation: live-card-arrive 5.2s cubic-bezier(.16, 1, .3, 1) both; }
.live-celebration-card::before, .live-celebration-card::after { content: ''; position: absolute; top: .55rem; bottom: .55rem; width: 1px; background: var(--live-green); opacity: .8; }
.live-celebration-card::before { left: .65rem; }
.live-celebration-card::after { right: .65rem; }
.live-celebration-eyebrow { display: flex; align-items: center; gap: .6rem; font-size: .62rem; font-weight: 800; letter-spacing: .26em; text-transform: uppercase; color: var(--live-green); }
.live-celebration-eyebrow i { width: 2.4rem; height: 1px; background: currentColor; }
.live-celebration-card strong { margin-top: .35rem; max-width: 22ch; font-size: clamp(1.8rem, 3.6vw, 3.4rem); line-height: 1; color: oklch(0.97 0.012 292); }
.live-celebration-card p { margin-top: .5rem; font-size: .78rem; color: oklch(0.74 0.04 292); }
.live-celebration-card p b { color: oklch(0.92 0.08 149); }
.live-celebration-score { display: flex; align-items: baseline; gap: .35rem; margin-top: .6rem; font-family: var(--font-mono); color: var(--live-green); }
.live-celebration-score span { font-size: 1.45rem; font-weight: 800; }
.live-celebration-score small { font-size: .55rem; letter-spacing: .18em; }
.live-celebration-progress { position: absolute; left: .65rem; right: .65rem; bottom: .42rem; height: 2px; background: rgba(255, 255, 255, .06); overflow: hidden; }
.live-celebration-progress i { display: block; height: 100%; background: var(--live-green); transform-origin: left; animation: live-celebration-progress 5.2s linear both; }
.live-blood-solve { color: var(--live-green); }
.live-blood-first { color: #fbbf24; }
.live-blood-second { color: #cbd5e1; }
.live-blood-third { color: #fb923c; }

/* 右侧面板。 */
.live-rail { display: flex; min-height: 0; flex-direction: column; gap: .75rem; }
.live-panel { display: flex; flex-direction: column; overflow: hidden; border: 1px solid var(--live-line); background: var(--live-panel); }
.live-panel-heading { display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid rgba(148, 163, 184, .12); padding: .75rem 1rem; font-size: .7rem; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; }
.live-panel-heading span { font-family: var(--font-mono); font-size: .55rem; color: #64748b; }
.live-rank-viewport { min-height: 0; flex: 1; overflow: hidden; }
.live-rank-track-scroll { animation: live-rank-scroll linear infinite; }
.live-ranking { padding: .4rem; }
.live-ranking li { display: flex; min-height: 2.55rem; align-items: center; gap: .7rem; border-bottom: 1px solid rgba(148, 163, 184, .08); padding: .35rem .55rem; font-size: .75rem; }
.live-ranking li:last-child { border-bottom: 0; }
.live-ranking strong { min-width: 4.5rem; text-align: right; color: var(--live-green); }
.live-rank { width: 1.5rem; font-family: var(--font-mono); font-weight: 700; color: #64748b; }
.live-rank-first { background: linear-gradient(90deg, rgba(251, 191, 36, .13), transparent); }
.live-rank-first .live-rank { color: #fbbf24; }
.live-rank-second .live-rank { color: #cbd5e1; }
.live-rank-third .live-rank { color: #fb923c; }
.live-feed { min-height: 0; overflow: hidden; padding: .4rem; }
.live-feed li { display: grid; grid-template-columns: 3.7rem auto minmax(0, 1fr); align-items: start; gap: .5rem; border-bottom: 1px solid rgba(148, 163, 184, .08); padding: .55rem .35rem; }
.live-feed time { padding-top: .12rem; font-family: var(--font-mono); font-size: .55rem; color: #475569; }
.live-feed > li > span { border: 1px solid currentColor; border-radius: 999px; padding: .06rem .32rem; font-size: .52rem; font-weight: 700; white-space: nowrap; }
.live-feed p { font-size: .66rem; line-height: 1.35; color: #94a3b8; }
.live-feed strong, .live-feed b { color: #e2e8f0; font-style: normal; }
.live-feed em { color: var(--live-green); font-family: var(--font-mono); font-style: normal; font-weight: 700; }
.live-empty { display: flex; align-items: center; justify-content: center; color: #64748b; font-size: .75rem; }

.live-celebration-enter-active, .live-celebration-leave-active { transition: opacity .42s cubic-bezier(.16, 1, .3, 1); }
.live-celebration-enter-from, .live-celebration-leave-to { opacity: 0; }

@keyframes live-dot-pulse { 0%, 100% { opacity: 1; } 50% { opacity: .35; } }
@keyframes live-rank-scroll { from { transform: translateY(0); } to { transform: translateY(-50%); } }
@keyframes live-flash { 0% { opacity: 0; } 16% { opacity: 1; } 100% { opacity: 0; } }
@keyframes live-impact-ring { 0% { opacity: 0; transform: translate(-50%, -50%) scale(.12); } 14% { opacity: 1; } 100% { opacity: 0; transform: translate(-50%, -50%) scale(5.4); } }
@keyframes live-particle { 0% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(1rem) scale(.5); } 18% { opacity: 1; } 100% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(var(--particle-distance)) scale(1); } }
@keyframes live-card-arrive { 0% { opacity: 0; transform: translateY(-1rem) scale(.84); clip-path: inset(0 50%); } 12% { opacity: 1; transform: translateY(0) scale(1.025); clip-path: inset(0); } 18%, 100% { opacity: 1; transform: scale(1); clip-path: inset(0); } }
@keyframes live-celebration-progress { from { transform: scaleX(1); } to { transform: scaleX(0); } }

@media (max-width: 900px) {
  .live-screen { min-height: 100svh; overflow: auto; }
  .live-screen-header { flex-wrap: wrap; }
  .live-screen-metrics { order: 3; width: 100%; justify-content: space-around; border-top: 1px solid rgba(148, 163, 184, .1); padding-top: .7rem; }
  .live-screen-layout { grid-template-columns: 1fr; min-height: 75rem; }
  .live-arena { min-height: 42rem; }
  .live-rail { min-height: 35rem; }
}
@media (prefers-reduced-motion: reduce) {
  .live-arena-pulse, .live-rank-track-scroll, .live-celebration-flash, .live-impact-ring, .live-celebration-particle, .live-celebration-card, .live-celebration-progress i { animation: none; }
  .live-celebration-enter-active, .live-celebration-leave-active { transition: none; }
  .live-celebration-card { opacity: 1; }
}
</style>
