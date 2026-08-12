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
const fullscreen = ref(false)
const clock = ref(Date.now())
let clockTimer: ReturnType<typeof setInterval> | undefined
let refreshTimer: ReturnType<typeof setInterval> | undefined
let projectionTimer: ReturnType<typeof setTimeout> | undefined
let featuredTimer: ReturnType<typeof setTimeout> | undefined
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
const latestSolve = computed(() => solveFeed.value[0] ?? null)
const dataAsOf = computed(() => leaderboard.value?.dataAsOf ?? leaderboard.value?.generatedAt)

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
  const previousLatestKey = latestSolve.value?.key
  leaderboard.value = leaderboardResult.data as NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse
  await nextTick()
  const currentLatest = latestSolve.value
  if (previousLatestKey && currentLatest && currentLatest.key !== previousLatestKey) {
    featuredSolve.value = currentLatest
    if (featuredTimer) clearTimeout(featuredTimer)
    featuredTimer = setTimeout(() => { featuredSolve.value = null }, 4_500)
  }
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
  if (featuredTimer) clearTimeout(featuredTimer)
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
      <section class="control-screen-arena" :aria-label="t('题目态势')">
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
            class="control-screen-tower-wrap"
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

        <Transition name="screen-burst" mode="out-in">
          <div v-if="featuredSolve" :key="featuredSolve.key" class="control-screen-burst">
            <p :class="bloodClass(featuredSolve.bloodRank)">{{ bloodLabel(featuredSolve.bloodRank) }}</p>
            <strong>{{ featuredSolve.teamName }}</strong>
            <span>{{ featuredSolve.challengeTitle }} · <b>+{{ featuredSolve.score }} pts</b></span>
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
  --screen-panel: rgba(11, 17, 31, .88);
  --screen-line: rgba(96, 165, 250, .18);
  position: relative;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  width: 100%;
  min-height: 100svh;
  overflow: hidden;
  color: #e5edf9;
  background: #070b14;
  color-scheme: dark;
}
.control-screen-grid {
  position: absolute;
  inset: 0;
  pointer-events: none;
  opacity: .42;
  background-image:
    linear-gradient(rgba(59, 130, 246, .035) 1px, transparent 1px),
    linear-gradient(90deg, rgba(59, 130, 246, .035) 1px, transparent 1px),
    radial-gradient(circle at 42% 22%, rgba(37, 99, 235, .18), transparent 38%);
  background-size: 48px 48px, 48px 48px, auto;
}
.control-screen-header,
.control-screen-footer {
  position: relative;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 1.5rem;
  border-color: rgba(148, 163, 184, .13);
  background: rgba(7, 11, 20, .92);
}
.control-screen-header { min-height: 5.5rem; border-bottom-width: 1px; padding: 1rem 1.5rem; }
.control-screen-footer { min-height: 2rem; border-top-width: 1px; padding: .4rem 1.5rem; font-size: .625rem; text-transform: uppercase; letter-spacing: .14em; color: #64748b; }
.control-screen-wordmark { font-family: var(--font-mono); font-weight: 700; letter-spacing: .18em; color: #60a5fa; text-transform: uppercase; }
.control-screen-divider { width: 1px; height: 1.75rem; background: rgba(148, 163, 184, .18); }
.control-screen-track { border: 1px solid rgba(148, 163, 184, .16); padding: .14rem .5rem; font-family: var(--font-mono); font-size: .55rem; color: #64748b; transition: border-color .15s ease, background-color .15s ease, color .15s ease; }
.control-screen-track:hover, .control-screen-track:focus-visible { border-color: rgba(96, 165, 250, .5); color: #cbd5e1; outline: none; }
.control-screen-track-active { border-color: rgba(96, 165, 250, .65); background: rgba(37, 99, 235, .14); color: #93c5fd; }
.control-screen-metrics { margin-left: auto; display: grid; grid-auto-flow: column; gap: 1.75rem; }
.control-screen-metrics div { min-width: 4rem; text-align: center; }
.control-screen-metrics dt { font-size: .625rem; text-transform: uppercase; letter-spacing: .18em; color: #64748b; }
.control-screen-metrics dd { margin-top: .2rem; font-family: var(--font-mono); font-size: 1.4rem; font-weight: 700; color: #f8fafc; }
.control-screen-metrics dd span { margin-left: .15rem; font-size: .7rem; color: #64748b; }
.control-screen-layout { position: relative; z-index: 1; display: grid; grid-template-columns: minmax(0, 1fr) clamp(18rem, 24vw, 27rem); min-height: 0; gap: .75rem; padding: .75rem; }
.control-screen-arena { position: relative; min-height: 0; overflow: hidden; border: 1px solid var(--screen-line); background: radial-gradient(circle at 50% 64%, rgba(37, 99, 235, .15), transparent 42%), rgba(4, 8, 18, .62); }
.control-screen-arena-heading { position: absolute; z-index: 5; inset: 1rem 1.25rem auto; display: flex; justify-content: space-between; gap: 1rem; font-size: .7rem; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #94a3b8; }
.control-screen-frozen { color: #fbbf24; }
.control-screen-orbit { position: absolute; border: 1px solid rgba(96, 165, 250, .14); border-radius: 999px; }
.control-screen-orbit-one { width: 72%; aspect-ratio: 1; left: -14%; top: 14%; transform: rotate(-12deg); }
.control-screen-orbit-two { width: 65%; aspect-ratio: 1; right: -28%; bottom: -34%; transform: rotate(18deg); }
.control-screen-scan { position: absolute; inset: 0; background: linear-gradient(180deg, transparent 0 46%, rgba(59, 130, 246, .08) 49%, transparent 52%); background-size: 100% 35%; animation: screen-scan 10s linear infinite; }
.control-screen-towers { position: absolute; inset: 5rem 4% 3.2rem; display: flex; align-items: flex-end; justify-content: center; gap: clamp(.55rem, 1.5vw, 1.5rem); perspective: 800px; }
.control-screen-tower-wrap { display: grid; grid-template-rows: auto 1fr auto auto; align-items: end; width: min(8.5rem, 11%); min-width: 4.7rem; height: 100%; animation: screen-rise .55s ease-out both; animation-delay: var(--screen-delay); }
.control-screen-tower-copy { margin-bottom: .5rem; display: flex; flex-direction: column; text-align: center; font-size: clamp(.58rem, .75vw, .78rem); }
.control-screen-tower-copy span:last-child { margin-top: .12rem; font-size: .65rem; }
.control-screen-tower { position: relative; width: 72%; min-height: 3rem; margin: 0 auto; border: 1px solid rgba(96, 165, 250, .55); background: repeating-linear-gradient(180deg, rgba(59, 130, 246, .22) 0 3px, rgba(15, 23, 42, .9) 3px 8px); box-shadow: inset 0 0 24px rgba(37, 99, 235, .25), 0 0 22px rgba(37, 99, 235, .12); transform: skewY(-3deg); }
.control-screen-tower-cap { position: absolute; inset: -.38rem -.16rem auto; height: .5rem; border: 1px solid rgba(147, 197, 253, .6); background: #2563eb; transform: skewY(8deg); box-shadow: 0 0 18px rgba(59, 130, 246, .65); }
.control-screen-tower-lines { position: absolute; inset: 0; background: linear-gradient(90deg, transparent 42%, rgba(147, 197, 253, .26) 43% 46%, transparent 47%); }
.control-screen-tower-meta { margin-top: .65rem; display: flex; flex-direction: column; align-items: center; gap: .25rem; }
.control-screen-tower-meta span:first-child { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; border: 1px solid currentColor; border-radius: 999px; padding: .08rem .45rem; font-size: .58rem; }
.control-screen-progress { width: 72%; height: 2px; margin: .45rem auto 0; overflow: hidden; background: rgba(148, 163, 184, .14); }
.control-screen-progress span { display: block; height: 100%; background: #60a5fa; box-shadow: 0 0 8px #3b82f6; transition: width .45s ease; }
.control-screen-burst { position: absolute; z-index: 7; left: 50%; top: 18%; display: flex; min-width: min(26rem, 70%); transform: translateX(-50%); flex-direction: column; align-items: center; border: 1px solid rgba(96, 165, 250, .36); background: rgba(7, 11, 20, .9); padding: 1rem 2.5rem; text-align: center; box-shadow: 0 0 45px rgba(37, 99, 235, .18); backdrop-filter: blur(10px); }
.control-screen-burst p { font-size: .65rem; font-weight: 800; letter-spacing: .32em; text-transform: uppercase; }
.control-screen-burst strong { margin-top: .25rem; font-size: clamp(1.35rem, 2vw, 2.2rem); }
.control-screen-burst span { margin-top: .15rem; font-size: .75rem; color: #94a3b8; }
.control-screen-burst b, .screen-blood-solve { color: #60a5fa; }
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
.screen-burst-enter-active, .screen-burst-leave-active { transition: opacity .35s ease, transform .35s ease; }
.screen-burst-enter-from, .screen-burst-leave-to { opacity: 0; transform: translate(-50%, -12px) scale(.98); }
@keyframes screen-scan { from { background-position-y: -50%; } to { background-position-y: 150%; } }
@keyframes screen-rise { from { opacity: 0; transform: translateY(18px); } to { opacity: 1; transform: translateY(0); } }
@media (max-width: 900px) {
  .control-screen { min-height: 100svh; overflow: auto; }
  .control-screen-header { flex-wrap: wrap; }
  .control-screen-metrics { order: 3; width: 100%; justify-content: space-around; border-top: 1px solid rgba(148,163,184,.1); padding-top: .7rem; }
  .control-screen-layout { grid-template-columns: 1fr; min-height: 75rem; }
  .control-screen-arena { min-height: 42rem; }
  .control-screen-rail { min-height: 35rem; }
}
@media (prefers-reduced-motion: reduce) {
  .control-screen-scan, .control-screen-tower-wrap { animation: none; }
  .screen-burst-enter-active, .screen-burst-leave-active { transition: none; }
}
</style>
