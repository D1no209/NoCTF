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
import { getCompetitionEndpoint, getLeaderboardEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardBloodRankProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '~/api'
import {
  controlScreenChallenges,
  controlScreenEntries,
  controlScreenSolveFeed,
  reconcileControlScreenSolves,
} from '~/utils/control-screen'
import type { ControlScreenSolve } from '~/utils/control-screen'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'

definePageMeta({ layout: false })

const route = useRoute()
const competitionId = route.params.id as string
const { configuration, ensureLoaded } = usePlatform()
const { t } = useLocale()

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const leaderboard = ref<NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null>(null)
const selectedTrackKey = ref('')
const loading = ref(true)
const refreshing = ref(false)
const projectionPending = ref(false)
const error = ref<string | null>(null)
const featuredSolve = ref<ControlScreenSolve | null>(null)
const celebrationQueue = ref<ControlScreenSolve[]>([])
const fullscreen = ref(false)
const clock = ref(Date.now())
let clockTimer: ReturnType<typeof setInterval> | undefined
let refreshTimer: ReturnType<typeof setInterval> | undefined
let projectionTimer: ReturnType<typeof setTimeout> | undefined
let celebrationTimer: ReturnType<typeof setTimeout> | undefined
let seenSolveKeys: Set<string> | null = null
let unwatch: (() => void) | undefined

const availableTracks = computed(() => (leaderboard.value?.tracks ?? [])
  .filter(track => !track.isInternal && track.visibleOnLeaderboard))

watch(availableTracks, (tracks) => {
  if (!tracks.length) {
    selectedTrackKey.value = ''
    return
  }
  if (!tracks.some(track => track.key === selectedTrackKey.value)) {
    selectedTrackKey.value = tracks[0]?.key ?? ''
  }
}, { immediate: true })

const entries = computed(() => controlScreenEntries(leaderboard.value, selectedTrackKey.value))
const rankedEntries = computed(() => [...entries.value]
  .sort((left, right) => (left.rank ?? Number.MAX_SAFE_INTEGER) - (right.rank ?? Number.MAX_SAFE_INTEGER)))
const visibleRanking = computed(() => rankedEntries.value.slice(0, 10))
const challenges = computed(() => controlScreenChallenges(leaderboard.value, entries.value))
const challengePageSize = 8
const challengePageCount = computed(() => Math.max(1, Math.ceil(challenges.value.length / challengePageSize)))
const challengePage = computed(() => Math.floor(clock.value / 12_000) % challengePageCount.value)
const visibleChallenges = computed(() => challenges.value.slice(
  challengePage.value * challengePageSize,
  (challengePage.value + 1) * challengePageSize,
))
const solveFeed = computed(() => controlScreenSolveFeed(leaderboard.value, entries.value).slice(0, 8))
const solvedChallengeCount = computed(() => challenges.value.filter(challenge => challenge.solveCount > 0).length)
const totalSolveCount = computed(() => challenges.value.reduce((sum, challenge) => sum + challenge.solveCount, 0))
const dataAsOf = computed(() => leaderboard.value?.dataAsOf ?? leaderboard.value?.generatedAt)
const featuredChallengeKey = computed(() => challengeKey(featuredSolve.value?.competitionChallengeId))

const celebrationParticles = Array.from({ length: 28 }, (_, index) => ({
  id: index,
  angle: `${index * (360 / 28)}deg`,
  distance: `${9 + (index % 5) * 1.7}rem`,
  delay: `${(index % 7) * 28}ms`,
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

const trackName = computed(() => availableTracks.value.find(track => track.key === selectedTrackKey.value)?.name)

function bloodLabel(rank: NoCtfapiEndpointsCompetitionsLeaderboardBloodRankProtocol | null): string {
  if (rank === 'First') return t('一血')
  if (rank === 'Second') return t('二血')
  if (rank === 'Third') return t('三血')
  return t('攻克')
}

function bloodClass(rank: NoCtfapiEndpointsCompetitionsLeaderboardBloodRankProtocol | null): string {
  if (rank === 'First') return 'screen-blood-first'
  if (rank === 'Second') return 'screen-blood-second'
  if (rank === 'Third') return 'screen-blood-third'
  return 'screen-blood-solve'
}

function rankClass(rank?: number): string {
  if (rank === 1) return 'screen-rank-first'
  if (rank === 2) return 'screen-rank-second'
  if (rank === 3) return 'screen-rank-third'
  return ''
}

function challengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

function playNextCelebration(): void {
  if (featuredSolve.value || !celebrationQueue.value.length) return
  featuredSolve.value = celebrationQueue.value.shift() ?? null
  if (!featuredSolve.value) return
  celebrationTimer = setTimeout(() => {
    featuredSolve.value = null
    celebrationTimer = setTimeout(playNextCelebration, 450)
  }, 5_200)
}

function reconcileCelebrations(currentSolves: readonly ControlScreenSolve[]): void {
  const reconciled = reconcileControlScreenSolves(seenSolveKeys, currentSolves)
  seenSolveKeys = reconciled.seenKeys
  if (!reconciled.newSolves.length) return
  celebrationQueue.value.push(...reconciled.newSolves)
  playNextCelebration()
}

async function loadData(): Promise<void> {
  refreshing.value = Boolean(competition.value || leaderboard.value)
  const [competitionResult, leaderboardResult] = await Promise.all([
    getCompetitionEndpoint({ path: { competitionId } }),
    getLeaderboardEndpoint({ path: { competitionId } }),
  ])
  loading.value = false
  refreshing.value = false

  if (competitionResult.error || !competitionResult.data) {
    error.value = parseApiError(competitionResult.error, t('加载竞赛失败')).message
    return
  }
  competition.value = competitionResult.data
  if (competition.value.mode !== 'Ctf') {
    projectionPending.value = false
    error.value = t('中控大屏当前仅支持 CTF 比赛')
    return
  }
  if (leaderboardResult.response?.status === 202) {
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
  if (leaderboardResult.error || !leaderboardResult.data) {
    projectionPending.value = false
    error.value = parseApiError(leaderboardResult.error, t('加载记分板失败')).message
    return
  }
  projectionPending.value = false
  error.value = null
  leaderboard.value = leaderboardResult.data as NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse
  await nextTick()
  reconcileCelebrations(controlScreenSolveFeed(leaderboard.value, entries.value))
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

watch(selectedTrackKey, () => {
  const currentSolves = controlScreenSolveFeed(leaderboard.value, entries.value)
  seenSolveKeys = new Set(currentSolves.map(solve => solve.key))
  celebrationQueue.value = []
  featuredSolve.value = null
  if (celebrationTimer) clearTimeout(celebrationTimer)
})

onMounted(async () => {
  await ensureLoaded()
  await refreshLatest()
  clockTimer = setInterval(() => { clock.value = Date.now() }, 1000)
  refreshTimer = setInterval(() => void refreshLatest(), 15_000)
  document.addEventListener('fullscreenchange', syncFullscreen)
  unwatch = watchCompetition(competitionId, {
    leaderboardRefreshed: () => void refreshLatest(),
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
})
</script>

<template>
  <div class="control-screen dark">
    <div class="control-screen-grid" aria-hidden="true" />
    <header class="control-screen-header">
      <div class="min-w-0">
        <div class="flex items-center gap-3">
          <span class="control-screen-wordmark">{{ configuration?.name ?? 'NoCTF' }}</span>
          <span class="control-screen-divider" aria-hidden="true" />
          <h1 class="truncate text-xl font-semibold tracking-tight lg:text-2xl">{{ competition?.title ?? t('竞赛中控') }}</h1>
        </div>
        <p class="mt-1 font-mono text-[0.625rem] uppercase tracking-[0.24em] text-slate-400 lg:text-xs">
          {{ t('现场赛事数据') }} · {{ trackName ?? t('综合赛道') }}
        </p>
        <div v-if="availableTracks.length > 1" class="mt-2 flex max-w-[38rem] flex-wrap gap-1.5">
          <button
            v-for="track in availableTracks"
            :key="track.key"
            type="button"
            :class="['control-screen-track', track.key === selectedTrackKey && 'control-screen-track-active']"
            @click="selectedTrackKey = track.key ?? ''"
          >
            {{ track.name }}
          </button>
        </div>
      </div>

      <dl class="control-screen-metrics">
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
          <p class="font-mono text-lg font-semibold tabular-nums text-primary lg:text-2xl">{{ remainingText }}</p>
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
          <NuxtLink :to="`/competitions/${competitionId}/leaderboard`" :aria-label="t('退出中控大屏')">
            <X class="size-4" />
          </NuxtLink>
        </Button>
      </div>
    </header>

    <main v-if="loading" class="control-screen-loading">
      <Radio class="size-8 animate-pulse text-primary" />
      <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('正在接入赛事数据') }}</p>
    </main>

    <main v-else-if="error" class="control-screen-loading">
      <ShieldCheck class="size-9 text-destructive" />
      <p class="max-w-lg text-center text-sm text-slate-300">{{ error }}</p>
      <Button variant="outline" class="border-white/15 bg-transparent text-white hover:bg-white/5" @click="refreshLatest">
        {{ t('重新加载') }}
      </Button>
    </main>

    <main v-else-if="projectionPending && !leaderboard" class="control-screen-loading">
      <Radio class="size-8 animate-pulse text-primary" />
      <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('记分板正在生成') }}</p>
    </main>

    <main v-else-if="leaderboard?.dataScope === 'Hidden'" class="control-screen-loading">
      <ShieldCheck class="size-9 text-primary" />
      <p class="text-xl font-semibold">{{ t('排行榜暂不公开') }}</p>
      <p class="text-sm text-slate-400">{{ t('主办方当前隐藏了排行榜数据') }}</p>
    </main>

    <main v-else class="control-screen-layout">
      <section
        :class="['control-screen-arena', featuredSolve && 'control-screen-arena-celebrating']"
        :aria-label="t('题目态势')"
      >
        <div class="control-screen-starfield" aria-hidden="true"><i v-for="index in 20" :key="index" /></div>
        <div class="control-screen-beam control-screen-beam-one" aria-hidden="true" />
        <div class="control-screen-beam control-screen-beam-two" aria-hidden="true" />
        <div class="control-screen-orbit control-screen-orbit-one" aria-hidden="true" />
        <div class="control-screen-orbit control-screen-orbit-two" aria-hidden="true" />
        <div class="control-screen-scan" aria-hidden="true" />

        <div class="control-screen-arena-heading">
          <div class="flex items-center gap-2">
            <span class="size-2 animate-pulse rounded-full bg-primary shadow-[0_0_18px_rgba(59,130,246,0.9)]" />
            <span>{{ t('实时题目态势') }}</span>
          </div>
          <div v-if="leaderboard?.dataScope === 'Frozen'" class="control-screen-frozen">
            {{ t('冻结快照') }} · {{ formatDateTime(dataAsOf) }}
          </div>
          <div v-else class="font-mono text-[0.625rem] text-slate-500">{{ formatDateTime(dataAsOf) }}</div>
          <div v-if="challengePageCount > 1" class="font-mono text-[0.625rem] text-slate-500">
            {{ t('第 {current} / {total} 组', { current: challengePage + 1, total: challengePageCount }) }}
          </div>
        </div>

        <div v-if="challenges.length" class="control-screen-towers">
          <article
            v-for="(challenge, index) in visibleChallenges"
            :key="challenge.competitionChallengeId"
            :class="[
              'control-screen-tower-wrap',
              featuredSolve && featuredChallengeKey !== challengeKey(challenge.competitionChallengeId) && 'control-screen-tower-muted',
              featuredChallengeKey === challengeKey(challenge.competitionChallengeId) && 'control-screen-tower-featured',
            ]"
            :style="{ '--screen-delay': `${index * 45}ms` }"
          >
            <div class="control-screen-tower-copy">
              <span class="truncate font-semibold">{{ challenge.title }}</span>
              <span class="font-mono text-primary">{{ challenge.currentScore }} pts</span>
            </div>
            <div class="control-screen-tower" :style="{ height: `${challenge.towerHeightPercent}%` }">
              <div class="control-screen-tower-cap" />
              <div class="control-screen-tower-lines" />
            </div>
            <div class="control-screen-tower-meta">
              <span :class="directionBadgeClass(challenge.direction)">{{ challenge.direction || t('未分类') }}</span>
              <span class="font-mono text-[0.625rem] tabular-nums text-slate-400">{{ challenge.solveCount }} {{ t('解出') }}</span>
            </div>
            <div class="control-screen-progress"><span :style="{ width: `${challenge.completionPercent}%` }" /></div>
          </article>
        </div>
        <div v-else class="control-screen-empty">{{ t('暂无已发布题目') }}</div>

        <Transition name="screen-celebration" mode="out-in">
          <div v-if="featuredSolve" :key="featuredSolve.key" class="control-screen-celebration" aria-live="assertive">
            <div class="control-screen-celebration-flash" aria-hidden="true" />
            <div class="control-screen-impact" aria-hidden="true">
              <i class="control-screen-impact-ring control-screen-impact-ring-one" />
              <i class="control-screen-impact-ring control-screen-impact-ring-two" />
              <i class="control-screen-impact-ring control-screen-impact-ring-three" />
              <span
                v-for="particle in celebrationParticles"
                :key="particle.id"
                class="control-screen-particle"
                :style="{
                  '--particle-angle': particle.angle,
                  '--particle-distance': particle.distance,
                  '--particle-delay': particle.delay,
                }"
              />
            </div>
            <div class="control-screen-celebration-card">
              <div class="control-screen-celebration-eyebrow">
                <span>{{ bloodLabel(featuredSolve.bloodRank) }}</span>
                <i />
                <span>{{ t('解题确认') }}</span>
              </div>
              <strong>{{ featuredSolve.teamName }}</strong>
              <p>{{ t('攻克了') }} <b>{{ featuredSolve.challengeTitle }}</b></p>
              <div class="control-screen-celebration-score">
                <span>+{{ featuredSolve.score }}</span>
                <small>PTS</small>
              </div>
              <div class="control-screen-celebration-progress" aria-hidden="true"><i /></div>
            </div>
          </div>
        </Transition>

        <div class="control-screen-corners" aria-hidden="true"><i /><i /><i /><i /></div>
      </section>

      <aside class="control-screen-rail">
        <section class="control-screen-panel min-h-0 flex-1">
          <header class="control-screen-panel-heading">
            <div class="flex items-center gap-2"><Trophy class="size-4 text-primary" />{{ t('排行榜') }}</div>
            <span>{{ trackName ?? t('综合') }}</span>
          </header>
          <ol v-if="visibleRanking.length" class="control-screen-ranking">
            <li v-for="entry in visibleRanking" :key="entry.teamId" :class="rankClass(entry.rank)">
              <span class="control-screen-rank">{{ entry.rank }}</span>
              <span class="min-w-0 flex-1 truncate font-semibold">{{ entry.teamName }}</span>
              <span class="font-mono text-[0.625rem] text-slate-500">{{ entry.solveCount ?? 0 }}</span>
              <strong class="font-mono tabular-nums">{{ entry.score ?? 0 }}</strong>
            </li>
          </ol>
          <div v-else class="control-screen-empty h-full">{{ t('还没有队伍得分') }}</div>
        </section>

        <section class="control-screen-panel min-h-0 flex-1">
          <header class="control-screen-panel-heading">
            <div class="flex items-center gap-2"><Radio class="size-4 text-primary" />{{ t('实时战报') }}</div>
            <span>LIVE</span>
          </header>
          <ol v-if="solveFeed.length" class="control-screen-feed">
            <li v-for="solve in solveFeed" :key="solve.key">
              <time :datetime="solve.solvedAt">{{ new Date(solve.solvedAt).toLocaleTimeString(localeTag(), { hour12: false }) }}</time>
              <span :class="bloodClass(solve.bloodRank)">{{ bloodLabel(solve.bloodRank) }}</span>
              <p><strong>{{ solve.teamName }}</strong> {{ t('攻克了') }} <b>{{ solve.challengeTitle }}</b> <em>+{{ solve.score }}</em></p>
            </li>
          </ol>
          <div v-else class="control-screen-empty h-full">{{ t('等待首个解题记录') }}</div>
        </section>
      </aside>
    </main>

    <footer class="control-screen-footer">
      <span class="flex items-center gap-2"><Radio class="size-3 text-primary" />{{ t('数据自动刷新') }}</span>
      <span class="hidden items-center gap-2 sm:flex"><Clock3 class="size-3" />{{ formatDateTime(dataAsOf) }}</span>
      <span class="ml-auto flex items-center gap-2"><Users class="size-3" />{{ entries.length }} {{ t('支队伍') }}</span>
    </footer>
  </div>
</template>

<style scoped>
.control-screen {
  --screen-panel: rgba(12, 8, 25, .94);
  --screen-line: rgba(158, 119, 237, .2);
  --screen-green: oklch(0.78 0.22 149);
  --screen-purple: oklch(0.67 0.19 300);
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
.control-screen-grid {
  position: absolute;
  inset: 0;
  pointer-events: none;
  opacity: .55;
  background-image:
    linear-gradient(rgba(158, 119, 237, .035) 1px, transparent 1px),
    linear-gradient(90deg, rgba(158, 119, 237, .035) 1px, transparent 1px),
    radial-gradient(circle at 40% 58%, rgba(30, 224, 109, .11), transparent 36%);
  background-size: 42px 42px, 42px 42px, auto;
}
.control-screen-header,
.control-screen-footer {
  position: relative;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 1.5rem;
  border-color: rgba(148, 163, 184, .13);
  background: rgba(7, 5, 16, .95);
}
.control-screen-header { min-height: 5.5rem; border-bottom-width: 1px; padding: 1rem 1.5rem; }
.control-screen-footer { min-height: 2rem; border-top-width: 1px; padding: .4rem 1.5rem; font-size: .625rem; text-transform: uppercase; letter-spacing: .14em; color: #64748b; }
.control-screen-wordmark { font-family: var(--font-mono); font-weight: 700; letter-spacing: .18em; color: var(--screen-purple); text-transform: uppercase; }
.control-screen-divider { width: 1px; height: 1.75rem; background: rgba(148, 163, 184, .18); }
.control-screen-track { border: 1px solid rgba(148, 163, 184, .16); padding: .14rem .5rem; font-family: var(--font-mono); font-size: .55rem; color: #64748b; transition: border-color .15s ease, background-color .15s ease, color .15s ease; }
.control-screen-track:hover, .control-screen-track:focus-visible { border-color: rgba(96, 165, 250, .5); color: #cbd5e1; outline: none; }
.control-screen-track-active { border-color: color-mix(in oklch, var(--screen-purple) 70%, transparent); background: rgba(115, 72, 173, .18); color: oklch(0.84 0.11 300); }
.control-screen-metrics { margin-left: auto; display: grid; grid-auto-flow: column; gap: 1.75rem; }
.control-screen-metrics div { min-width: 4rem; text-align: center; }
.control-screen-metrics dt { font-size: .625rem; text-transform: uppercase; letter-spacing: .18em; color: #64748b; }
.control-screen-metrics dd { margin-top: .2rem; font-family: var(--font-mono); font-size: 1.4rem; font-weight: 700; color: #f8fafc; }
.control-screen-metrics dd span { margin-left: .15rem; font-size: .7rem; color: #64748b; }
.control-screen-layout { position: relative; z-index: 1; display: grid; grid-template-columns: minmax(0, 1fr) clamp(18rem, 24vw, 27rem); min-height: 0; gap: .75rem; padding: .75rem; }
.control-screen-arena { position: relative; min-height: 0; overflow: hidden; border: 1px solid var(--screen-line); background: radial-gradient(circle at 50% 72%, rgba(30, 224, 109, .12), transparent 34%), radial-gradient(circle at 42% 34%, rgba(130, 80, 198, .12), transparent 38%), rgba(5, 3, 14, .72); isolation: isolate; }
.control-screen-arena-heading { position: absolute; z-index: 5; inset: 1rem 1.25rem auto; display: flex; justify-content: space-between; gap: 1rem; font-size: .7rem; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #94a3b8; }
.control-screen-frozen { color: #fbbf24; }
.control-screen-orbit { position: absolute; border: 1px solid rgba(154, 103, 226, .22); border-radius: 999px; animation: screen-orbit-drift 16s ease-in-out infinite alternate; }
.control-screen-orbit-one { width: 72%; aspect-ratio: 1; left: -14%; top: 14%; transform: rotate(-12deg); }
.control-screen-orbit-two { width: 65%; aspect-ratio: 1; right: -28%; bottom: -34%; transform: rotate(18deg); }
.control-screen-scan { position: absolute; inset: 0; background: linear-gradient(180deg, transparent 0 46%, rgba(31, 220, 112, .065) 49%, transparent 52%); background-size: 100% 35%; animation: screen-scan 10s linear infinite; }
.control-screen-towers { position: absolute; inset: 5rem 4% 3.2rem; display: flex; align-items: flex-end; justify-content: center; gap: clamp(.55rem, 1.5vw, 1.5rem); perspective: 900px; transform-origin: 50% 75%; animation: screen-camera-cruise 18s cubic-bezier(.45, 0, .55, 1) infinite alternate; transition: transform .9s cubic-bezier(.16, 1, .3, 1), filter .8s ease; }
.control-screen-tower-wrap { position: relative; display: grid; grid-template-rows: auto 1fr auto auto; align-items: end; width: min(8.5rem, 11%); min-width: 4.7rem; height: 100%; transform-origin: 50% 100%; animation: screen-rise .55s cubic-bezier(.16, 1, .3, 1) both; animation-delay: var(--screen-delay); transition: opacity .65s ease, filter .65s ease, transform .85s cubic-bezier(.16, 1, .3, 1); }
.control-screen-tower-copy { margin-bottom: .5rem; display: flex; flex-direction: column; text-align: center; font-size: clamp(.58rem, .75vw, .78rem); }
.control-screen-tower-copy span:last-child { margin-top: .12rem; font-size: .65rem; }
.control-screen-tower { position: relative; width: 72%; min-height: 3rem; margin: 0 auto; border: 1px solid rgba(39, 219, 113, .55); background: repeating-linear-gradient(180deg, rgba(28, 210, 102, .2) 0 3px, rgba(8, 22, 19, .94) 3px 8px); box-shadow: inset 0 0 24px rgba(25, 201, 95, .22), 0 0 24px rgba(23, 194, 91, .14); transform: skewY(-3deg); }
.control-screen-tower-wrap:nth-child(3n) .control-screen-tower { border-color: rgba(157, 98, 229, .58); background: repeating-linear-gradient(180deg, rgba(133, 75, 197, .22) 0 3px, rgba(20, 10, 35, .94) 3px 8px); box-shadow: inset 0 0 24px rgba(139, 83, 202, .23), 0 0 24px rgba(118, 61, 184, .14); }
.control-screen-tower-cap { position: absolute; inset: -.38rem -.16rem auto; height: .5rem; border: 1px solid rgba(159, 255, 198, .72); background: var(--screen-green); transform: skewY(8deg); box-shadow: 0 0 22px rgba(33, 232, 117, .72); }
.control-screen-tower-wrap:nth-child(3n) .control-screen-tower-cap { border-color: rgba(225, 198, 255, .7); background: var(--screen-purple); box-shadow: 0 0 22px rgba(164, 98, 236, .68); }
.control-screen-tower-lines { position: absolute; inset: 0; background: linear-gradient(90deg, transparent 42%, rgba(148, 255, 190, .22) 43% 46%, transparent 47%); }
.control-screen-tower-meta { margin-top: .65rem; display: flex; flex-direction: column; align-items: center; gap: .25rem; }
.control-screen-tower-meta span:first-child { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; border: 1px solid currentColor; border-radius: 999px; padding: .08rem .45rem; font-size: .58rem; }
.control-screen-progress { width: 72%; height: 2px; margin: .45rem auto 0; overflow: hidden; background: rgba(148, 163, 184, .14); }
.control-screen-progress span { display: block; height: 100%; background: var(--screen-green); box-shadow: 0 0 8px rgba(33, 232, 117, .75); transition: width .45s ease; }
.control-screen-starfield { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
.control-screen-starfield i { position: absolute; left: 10%; top: 18%; width: 3px; height: 3px; background: var(--screen-green); box-shadow: 0 0 10px currentColor; opacity: .22; animation: screen-star-drift 9s linear infinite; }
.control-screen-starfield i:nth-child(10n+2) { left: 24%; top: 72%; animation-delay: -2s; color: var(--screen-purple); }
.control-screen-starfield i:nth-child(10n+3) { left: 38%; top: 31%; animation-delay: -5s; }
.control-screen-starfield i:nth-child(10n+4) { left: 52%; top: 83%; animation-delay: -7s; color: var(--screen-purple); }
.control-screen-starfield i:nth-child(10n+5) { left: 66%; top: 16%; animation-delay: -4s; }
.control-screen-starfield i:nth-child(10n+6) { left: 79%; top: 61%; animation-delay: -1s; color: var(--screen-purple); }
.control-screen-starfield i:nth-child(10n+7) { left: 91%; top: 37%; animation-delay: -6s; }
.control-screen-starfield i:nth-child(10n+8) { left: 17%; top: 48%; animation-delay: -3s; color: var(--screen-purple); }
.control-screen-starfield i:nth-child(10n+9) { left: 45%; top: 12%; animation-delay: -8s; }
.control-screen-starfield i:nth-child(10n) { left: 72%; top: 91%; animation-delay: -4.5s; color: var(--screen-purple); }
.control-screen-beam { position: absolute; width: 30%; height: 130%; top: -20%; opacity: .12; transform: rotate(18deg); transform-origin: 50% 100%; background: linear-gradient(90deg, transparent, rgba(39, 226, 118, .72), transparent); filter: blur(3px); animation: screen-beam-sweep 13s ease-in-out infinite alternate; }
.control-screen-beam-one { left: 14%; }
.control-screen-beam-two { right: 4%; opacity: .09; background: linear-gradient(90deg, transparent, rgba(165, 100, 235, .75), transparent); animation-delay: -7s; }
.control-screen-tower-muted { opacity: .24; filter: saturate(.4) blur(.6px); transform: scale(.93); }
.control-screen-tower-featured { z-index: 5; transform: translateY(-1.2rem) scale(1.16); filter: brightness(1.35) saturate(1.18); }
.control-screen-tower-featured::after { content: ''; position: absolute; left: 50%; bottom: 2.2rem; width: 110%; aspect-ratio: 1; transform: translateX(-50%) rotateX(68deg); border: 2px solid var(--screen-green); border-radius: 50%; box-shadow: 0 0 26px rgba(33, 232, 117, .55), inset 0 0 18px rgba(33, 232, 117, .25); animation: screen-target-pulse .9s ease-out infinite; }
.control-screen-arena-celebrating .control-screen-towers { animation-play-state: paused; transform: scale(1.055) translateY(1.5%); }
.control-screen-celebration { position: absolute; z-index: 9; inset: 0; display: grid; place-items: center; pointer-events: none; }
.control-screen-celebration-flash { position: absolute; inset: 0; background: radial-gradient(circle at center, rgba(111, 255, 169, .32), transparent 48%); animation: screen-flash 1.15s ease-out both; }
.control-screen-impact { position: absolute; left: 50%; top: 59%; width: 1px; height: 1px; }
.control-screen-impact-ring { position: absolute; left: 0; top: 0; width: 5rem; aspect-ratio: 1; border: 2px solid var(--screen-green); border-radius: 50%; transform: translate(-50%, -50%) scale(.12); box-shadow: 0 0 18px rgba(36, 231, 118, .5); animation: screen-impact-ring 1.5s cubic-bezier(.16, 1, .3, 1) both; }
.control-screen-impact-ring-two { animation-delay: .18s; border-color: var(--screen-purple); }
.control-screen-impact-ring-three { animation-delay: .36s; }
.control-screen-particle { --particle-angle: 0deg; --particle-distance: 12rem; --particle-delay: 0ms; position: absolute; left: 0; top: 0; width: .35rem; height: .35rem; background: var(--screen-green); box-shadow: 0 0 12px rgba(38, 239, 126, .85); animation: screen-particle 1.25s cubic-bezier(.16, 1, .3, 1) var(--particle-delay) both; }
.control-screen-particle:nth-of-type(3n) { width: .24rem; height: .7rem; background: var(--screen-purple); box-shadow: 0 0 12px rgba(170, 104, 236, .85); }
.control-screen-celebration-card { position: relative; display: flex; min-width: min(31rem, 74%); flex-direction: column; align-items: center; border: 1px solid rgba(193, 153, 244, .55); background: rgba(9, 5, 19, .94); padding: 1.45rem 3rem 1.25rem; text-align: center; box-shadow: 0 0 0 1px rgba(41, 230, 120, .12), 0 0 58px rgba(93, 40, 153, .3); animation: screen-card-arrive 5.2s cubic-bezier(.16, 1, .3, 1) both; }
.control-screen-celebration-card::before, .control-screen-celebration-card::after { content: ''; position: absolute; top: .55rem; bottom: .55rem; width: 1px; background: var(--screen-green); opacity: .8; }
.control-screen-celebration-card::before { left: .65rem; }
.control-screen-celebration-card::after { right: .65rem; }
.control-screen-celebration-eyebrow { display: flex; align-items: center; gap: .6rem; font-size: .62rem; font-weight: 800; letter-spacing: .26em; text-transform: uppercase; color: var(--screen-green); }
.control-screen-celebration-eyebrow i { width: 2.4rem; height: 1px; background: currentColor; }
.control-screen-celebration-card strong { margin-top: .35rem; max-width: 22ch; font-size: clamp(1.8rem, 3.6vw, 3.4rem); line-height: 1; color: oklch(0.97 0.012 292); }
.control-screen-celebration-card p { margin-top: .5rem; font-size: .78rem; color: oklch(0.74 0.04 292); }
.control-screen-celebration-card p b { color: oklch(0.92 0.08 149); }
.control-screen-celebration-score { display: flex; align-items: baseline; gap: .35rem; margin-top: .6rem; font-family: var(--font-mono); color: var(--screen-green); }
.control-screen-celebration-score span { font-size: 1.45rem; font-weight: 800; }
.control-screen-celebration-score small { font-size: .55rem; letter-spacing: .18em; }
.control-screen-celebration-progress { position: absolute; left: .65rem; right: .65rem; bottom: .42rem; height: 2px; background: rgba(255, 255, 255, .06); overflow: hidden; }
.control-screen-celebration-progress i { display: block; height: 100%; background: var(--screen-green); transform-origin: left; animation: screen-celebration-progress 5.2s linear both; }
.screen-blood-solve { color: var(--screen-green); }
.screen-blood-first { color: #fbbf24; }
.screen-blood-second { color: #cbd5e1; }
.screen-blood-third { color: #fb923c; }
.control-screen-corners i { position: absolute; width: 1.4rem; height: 1.4rem; border-color: rgba(96, 165, 250, .65); }
.control-screen-corners i:nth-child(1) { left: .6rem; top: .6rem; border-left: 2px solid; border-top: 2px solid; }
.control-screen-corners i:nth-child(2) { right: .6rem; top: .6rem; border-right: 2px solid; border-top: 2px solid; }
.control-screen-corners i:nth-child(3) { left: .6rem; bottom: .6rem; border-left: 2px solid; border-bottom: 2px solid; }
.control-screen-corners i:nth-child(4) { right: .6rem; bottom: .6rem; border-right: 2px solid; border-bottom: 2px solid; }
.control-screen-rail { display: flex; min-height: 0; flex-direction: column; gap: .75rem; }
.control-screen-panel { display: flex; flex-direction: column; overflow: hidden; border: 1px solid var(--screen-line); background: var(--screen-panel); }
.control-screen-panel-heading { display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid rgba(148, 163, 184, .12); padding: .75rem 1rem; font-size: .7rem; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; }
.control-screen-panel-heading span { font-family: var(--font-mono); font-size: .55rem; color: #64748b; }
.control-screen-ranking, .control-screen-feed { min-height: 0; overflow: hidden; padding: .4rem; }
.control-screen-ranking li { display: flex; min-height: 2.55rem; align-items: center; gap: .7rem; border-bottom: 1px solid rgba(148, 163, 184, .08); padding: .35rem .55rem; font-size: .75rem; }
.control-screen-ranking li:last-child { border-bottom: 0; }
.control-screen-ranking strong { min-width: 4.5rem; text-align: right; color: #60a5fa; }
.control-screen-rank { width: 1.5rem; font-family: var(--font-mono); font-weight: 700; color: #64748b; }
.screen-rank-first { background: linear-gradient(90deg, rgba(251, 191, 36, .13), transparent); }
.screen-rank-first .control-screen-rank { color: #fbbf24; }
.screen-rank-second .control-screen-rank { color: #cbd5e1; }
.screen-rank-third .control-screen-rank { color: #fb923c; }
.control-screen-feed li { display: grid; grid-template-columns: 3.7rem auto minmax(0, 1fr); align-items: start; gap: .5rem; border-bottom: 1px solid rgba(148, 163, 184, .08); padding: .55rem .35rem; }
.control-screen-feed time { padding-top: .12rem; font-family: var(--font-mono); font-size: .55rem; color: #475569; }
.control-screen-feed > li > span { border: 1px solid currentColor; border-radius: 999px; padding: .06rem .32rem; font-size: .52rem; font-weight: 700; white-space: nowrap; }
.control-screen-feed p { font-size: .66rem; line-height: 1.35; color: #94a3b8; }
.control-screen-feed strong, .control-screen-feed b { color: #e2e8f0; font-style: normal; }
.control-screen-feed em { color: #60a5fa; font-family: var(--font-mono); font-style: normal; font-weight: 700; }
.control-screen-empty, .control-screen-loading { display: flex; align-items: center; justify-content: center; color: #64748b; }
.control-screen-loading { position: relative; z-index: 2; min-height: 0; flex-direction: column; gap: 1rem; }
.screen-celebration-enter-active, .screen-celebration-leave-active { transition: opacity .42s cubic-bezier(.16, 1, .3, 1); }
.screen-celebration-enter-from, .screen-celebration-leave-to { opacity: 0; }
@keyframes screen-scan { from { background-position-y: -50%; } to { background-position-y: 150%; } }
@keyframes screen-rise { from { opacity: 0; transform: translateY(18px); } to { opacity: 1; transform: translateY(0); } }
@keyframes screen-camera-cruise { from { transform: translate3d(-1.5%, 1.2%, 0) rotateY(-2deg) scale(.985); } to { transform: translate3d(1.5%, -1%, 0) rotateY(2deg) scale(1.025); } }
@keyframes screen-orbit-drift { from { translate: -1.5% 1%; scale: .98; } to { translate: 1.5% -1%; scale: 1.03; } }
@keyframes screen-star-drift { 0% { translate: 0 2rem; opacity: 0; } 18%, 78% { opacity: .5; } 100% { translate: 1.8rem -7rem; opacity: 0; } }
@keyframes screen-beam-sweep { from { transform: translateX(-26%) rotate(12deg); } to { transform: translateX(30%) rotate(22deg); } }
@keyframes screen-target-pulse { from { opacity: .85; transform: translateX(-50%) rotateX(68deg) scale(.55); } to { opacity: 0; transform: translateX(-50%) rotateX(68deg) scale(1.3); } }
@keyframes screen-flash { 0% { opacity: 0; } 16% { opacity: 1; } 100% { opacity: 0; } }
@keyframes screen-impact-ring { 0% { opacity: 0; transform: translate(-50%, -50%) scale(.12); } 14% { opacity: 1; } 100% { opacity: 0; transform: translate(-50%, -50%) scale(5.4); } }
@keyframes screen-particle { 0% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(1rem) scale(.5); } 18% { opacity: 1; } 100% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(var(--particle-distance)) scale(1); } }
@keyframes screen-card-arrive { 0% { opacity: 0; transform: translateY(-1rem) scale(.84); clip-path: inset(0 50%); } 12% { opacity: 1; transform: translateY(0) scale(1.025); clip-path: inset(0); } 18%, 100% { opacity: 1; transform: scale(1); clip-path: inset(0); } }
@keyframes screen-celebration-progress { from { transform: scaleX(1); } to { transform: scaleX(0); } }
@media (max-width: 900px) {
  .control-screen { min-height: 100svh; overflow: auto; }
  .control-screen-header { flex-wrap: wrap; }
  .control-screen-metrics { order: 3; width: 100%; justify-content: space-around; border-top: 1px solid rgba(148,163,184,.1); padding-top: .7rem; }
  .control-screen-layout { grid-template-columns: 1fr; min-height: 75rem; }
  .control-screen-arena { min-height: 42rem; }
  .control-screen-rail { min-height: 35rem; }
}
@media (prefers-reduced-motion: reduce) {
  .control-screen-scan, .control-screen-tower-wrap, .control-screen-towers, .control-screen-starfield i, .control-screen-beam, .control-screen-orbit, .control-screen-tower-featured::after, .control-screen-celebration-flash, .control-screen-impact-ring, .control-screen-particle, .control-screen-celebration-card, .control-screen-celebration-progress i { animation: none; }
  .control-screen-tower-wrap, .control-screen-towers { transition: none; }
  .screen-celebration-enter-active, .screen-celebration-leave-active { transition: none; }
  .control-screen-celebration-card { opacity: 1; }
}
</style>
