<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdLivePageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdLivePage'

const viewProps = defineProps<{ state: CompetitionsByIdLivePageViewState }>()
const { Clock3, Expand, Minimize, Radio, RefreshCw, ShieldCheck, Trophy, Users, X, scoreboardRankingStateLabel, scoreboardTeamSolveCount, competitionId, configuration, t, board, competition, loading, refreshing, projectionPending, error, featuredSolve, fullscreen, entries, rankedEntries, challenges, solveFeed, solvedChallengeCount, totalSolveCount, dataAsOf, marqueeEnabled, marqueeDuration, celebrationParticles, remainingText, elapsedText, bloodLabel, bloodClass, rankClass, refreshLatest, toggleFullscreen, setArenaRefRef } = toRefs(viewProps.state)
</script>

<template>
  <div class="live-screen dark">
    <div class="live-screen-grid" aria-hidden="true" />
    <div class="live-screen-aurora" aria-hidden="true" />

    <header class="live-screen-header">
      <div class="live-screen-title min-w-0">
        <div class="flex items-center gap-3">
          <span class="live-screen-wordmark">{{ configuration?.name ?? $t('common.label.noctf') }}</span>
          <span class="live-screen-divider" aria-hidden="true" />
          <h1 class="truncate text-xl font-semibold tracking-tight lg:text-2xl">{{ competition?.title ?? t('leaderboard.ctf.liveTitle') }}</h1>
        </div>
        <p class="mt-1 font-mono text-[0.625rem] uppercase tracking-[0.24em] text-slate-400 lg:text-xs">
          {{ t('competitions.label.liveDCityView') }} · {{ t('competitions.label.publicTracks') }}
        </p>
      </div>

      <dl class="live-screen-metrics">
        <div>
          <dt>{{ t('competitions.label.breached') }}</dt>
          <dd>{{ solvedChallengeCount }}<span>/{{ challenges.length }}</span></dd>
        </div>
        <div>
          <dt>{{ t('competitions.label.captures') }}</dt>
          <dd>{{ totalSolveCount }}</dd>
        </div>
        <div>
          <dt>{{ t('common.label.teams') }}</dt>
          <dd>{{ entries.length }}</dd>
        </div>
      </dl>

      <div class="live-screen-controls flex items-center gap-3">
        <div class="hidden text-right sm:block">
          <p class="text-[0.625rem] uppercase tracking-[0.2em] text-slate-500">{{ t('competitions.label.ends.livePageView') }}</p>
          <p class="live-screen-countdown font-mono text-lg font-semibold tabular-nums lg:text-2xl">{{ remainingText }}</p>
        </div>
        <div class="hidden h-9 w-px bg-white/10 lg:block" />
        <div class="hidden text-right lg:block">
          <p class="text-[0.625rem] uppercase tracking-[0.2em] text-slate-500">{{ t('competitions.label.elapsed') }}</p>
          <p class="font-mono text-sm tabular-nums text-slate-300">{{ elapsedText }}</p>
        </div>
        <Button variant="ghost" size="icon" class="text-slate-400 hover:bg-white/5 hover:text-white" :aria-label="t('competitions.label.refreshControlScreen')" @click="refreshLatest">
          <RefreshCw :class="['size-4', refreshing && 'animate-spin']" />
        </Button>
        <Button variant="ghost" size="icon" class="text-slate-400 hover:bg-white/5 hover:text-white" :aria-label="fullscreen ? t('competitions.label.exitFullscreen') : t('competitions.label.enterFullscreen')" @click="toggleFullscreen">
          <Minimize v-if="fullscreen" class="size-4" />
          <Expand v-else class="size-4" />
        </Button>
        <Button variant="ghost" size="icon" as-child class="text-slate-400 hover:bg-white/5 hover:text-white">
          <NuxtLink :to="`/competitions/${competitionId}/leaderboard`" :aria-label="t('competitions.label.exitDLiveScreen')">
            <X class="size-4" />
          </NuxtLink>
        </Button>
      </div>
    </header>

    <main class="live-screen-layout">
      <section
        :ref="setArenaRefRef"
        :class="['live-arena', featuredSolve && 'live-arena-focusing']"
        :aria-label="t('competitions.label.liveChallengeStatus')"
      >
        <div class="live-arena-scanlines" aria-hidden="true" />
        <div class="live-arena-vignette" aria-hidden="true" />

        <div v-if="loading" class="live-overlay">
          <Radio class="live-overlay-icon size-9 animate-pulse" />
          <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('competitions.competitionsBy.label.connectingLiveCompetitionData') }}</p>
        </div>

        <div v-else-if="error" class="live-overlay">
          <ShieldCheck class="size-9 text-destructive" />
          <p class="max-w-lg text-center text-sm text-slate-300">{{ $message(error) }}</p>
          <Button variant="outline" class="border-white/15 bg-transparent text-white hover:bg-white/5" @click="refreshLatest">
            {{ t('common.label.reload') }}
          </Button>
        </div>

        <div v-else-if="projectionPending && !board.snapshot.value" class="live-overlay">
          <Radio class="live-overlay-icon size-9 animate-pulse" />
          <p class="font-mono text-sm uppercase tracking-[0.22em] text-slate-400">{{ t('competitions.label.buildingScoreboardProjection') }}</p>
        </div>

        <div v-else-if="board.snapshot.value?.dataScope === 'Hidden'" class="live-overlay">
          <ShieldCheck class="live-overlay-icon size-9" />
          <p class="text-xl font-semibold">{{ t('common.competitionsBy.description.rankingsPublicYet') }}</p>
          <p class="text-sm text-slate-400">{{ t('common.competitionsBy.description.organizerCurrentlyHidesRanking') }}</p>
        </div>

        <div class="live-arena-heading" aria-hidden="true">
          <div class="flex items-center gap-2">
            <span class="live-arena-pulse" />
            <span>{{ t('competitions.label.liveChallengeStatus') }}</span>
          </div>
          <div v-if="board.snapshot.value?.dataScope === 'Frozen'" class="live-frozen">
            {{ t('competitions.label.frozenSnapshot') }} · {{ formatDateTime(dataAsOf) }}
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
                <span>{{ t('competitions.label.solveConfirmed') }}</span>
              </div>
              <strong>{{ featuredSolve.teamName }}</strong>
              <p>{{ t('competitions.label.solved.livePageView') }} <b>{{ featuredSolve.challengeTitle }}</b></p>
              <div class="live-celebration-score">
                <span>+{{ featuredSolve.score }}</span>
                <small>{{ $t('competitions.label.pts') }}</small>
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
            <div class="flex items-center gap-2"><Trophy class="size-4 text-primary" />{{ t('common.label.leaderboard.leaderboardPageView') }}</div>
            <span>{{ t('competitions.label.combined') }}</span>
          </header>
          <div v-if="rankedEntries.length" class="live-rank-viewport">
            <div
              :class="['live-rank-track', marqueeEnabled && 'live-rank-track-scroll']"
              :style="marqueeEnabled ? { animationDuration: `${marqueeDuration}s` } : undefined"
            >
              <ol class="live-ranking">
                <li v-for="entry in rankedEntries" :key="entry.teamId" :class="rankClass(entry.rank ?? undefined)">
                  <span class="live-rank">{{ entry.rank ?? $t('common.label.symbol') }}</span>
                  <span class="min-w-0 flex-1 truncate font-semibold">{{ entry.teamName }}<small v-if="entry.rankingState !== 'Eligible'"> · {{ scoreboardRankingStateLabel(entry.rankingState) }}</small></span>
                  <span class="font-mono text-[0.625rem] text-slate-500">{{ scoreboardTeamSolveCount(entry) }}</span>
                  <strong class="font-mono tabular-nums">{{ entry.totalScore ?? 0 }}</strong>
                </li>
              </ol>
              <ol v-if="marqueeEnabled" class="live-ranking" aria-hidden="true">
                <li v-for="entry in rankedEntries" :key="`clone-${entry.teamId}`" :class="rankClass(entry.rank ?? undefined)">
                  <span class="live-rank">{{ entry.rank ?? $t('common.label.symbol') }}</span>
                  <span class="min-w-0 flex-1 truncate font-semibold">{{ entry.teamName }}<small v-if="entry.rankingState !== 'Eligible'"> · {{ scoreboardRankingStateLabel(entry.rankingState) }}</small></span>
                  <span class="font-mono text-[0.625rem] text-slate-500">{{ scoreboardTeamSolveCount(entry) }}</span>
                  <strong class="font-mono tabular-nums">{{ entry.totalScore ?? 0 }}</strong>
                </li>
              </ol>
            </div>
          </div>
          <div v-else class="live-empty h-full">{{ t('common.competitionsBy.label.teamScoredYet') }}</div>
        </section>

        <section class="live-panel min-h-0 flex-1">
          <header class="live-panel-heading">
            <div class="flex items-center gap-2"><Radio class="size-4 text-primary" />{{ t('competitions.label.liveFeed') }}</div>
            <span>{{ $t('competitions.label.live') }}</span>
          </header>
          <ScrollSurface as="ol" v-if="solveFeed.length" class="live-feed">
            <li v-for="solve in solveFeed" :key="solve.key">
              <time :datetime="solve.solvedAt">{{ new Date(solve.solvedAt).toLocaleTimeString(localeTag(), { hour12: false }) }}</time>
              <span :class="bloodClass(solve.bloodRank)">{{ bloodLabel(solve.bloodRank) }}</span>
              <p><strong>{{ solve.teamName }}</strong> {{ t('competitions.label.solved.livePageView') }} <b>{{ solve.challengeTitle }}</b> <em>+{{ solve.score }}</em></p>
            </li>
          </ScrollSurface>
          <div v-else class="live-empty h-full">{{ t('competitions.competitionsBy.label.waitingFirstSolve') }}</div>
        </section>
      </aside>
    </main>

    <footer class="live-screen-footer">
      <span class="flex items-center gap-2"><Radio class="size-3 text-primary" />{{ t('competitions.label.dataRefreshesAutomatically') }}</span>
      <span class="hidden items-center gap-2 sm:flex"><Clock3 class="size-3" />{{ formatDateTime(dataAsOf) }}</span>
      <span class="ml-auto flex items-center gap-2"><Users class="size-3" />{{ entries.length }} {{ t('competitions.label.teams.livePageView') }}</span>
    </footer>
  </div>
</template>

<style scoped>
.live-screen {
  --live-city-background: oklch(0.075 0.028 285);
  --live-city-facade-top: oklch(0.145 0.045 272);
  --live-city-facade-bottom: oklch(0.09 0.028 284);
  --live-city-locked: oklch(0.65 0.205 252);
  --live-city-solved: oklch(0.62 0.25 24);
  --live-city-accent: oklch(0.68 0.2 302);
  --live-city-grid-major: oklch(0.48 0.14 300);
  --live-city-grid-minor: oklch(0.37 0.11 254);
  --live-city-backdrop: oklch(0.43 0.1 292);
  --live-city-radar: oklch(0.72 0.19 155);
  --live-green: oklch(0.78 0.22 149);
  --live-purple: var(--live-city-accent);
  --live-blue: var(--live-city-locked);
  --live-red: var(--live-city-solved);
  --background: var(--live-city-background);
  --card: var(--live-city-facade-top);
  --primary: var(--live-blue);
  --destructive: var(--live-red);
  --success: var(--live-green);
  --live-panel: color-mix(in oklch, var(--live-city-background) 94%, transparent);
  --live-line: color-mix(in oklch, var(--live-city-accent) 34%, transparent);
  position: relative;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  width: 100%;
  height: 100dvh;
  min-height: 0;
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
    linear-gradient(color-mix(in oklch, var(--primary) 4%, transparent) 1px, transparent 1px),
    linear-gradient(90deg, color-mix(in oklch, var(--primary) 4%, transparent) 1px, transparent 1px);
  background-size: 42px 42px;
}
.live-screen-aurora {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background:
    radial-gradient(circle at 18% 12%, color-mix(in oklch, var(--primary) 10%, transparent), transparent 34%),
    radial-gradient(circle at 84% 78%, color-mix(in oklch, var(--success) 7%, transparent), transparent 38%);
}
.live-screen-header,
.live-screen-footer {
  position: relative;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 1.5rem;
  border-color: color-mix(in oklch, var(--primary) 13%, transparent);
  background: color-mix(in oklch, var(--background) 95%, transparent);
}
.live-screen-header { min-height: 5.5rem; border-bottom-width: 1px; padding: 1rem 1.5rem; }
.live-screen-title { flex: 1; }
.live-screen-controls { flex-shrink: 0; }
.live-screen-footer { min-height: 2rem; border-top-width: 1px; padding: .4rem 1.5rem; font-size: .625rem; text-transform: uppercase; letter-spacing: .14em; color: var(--primary); }
.live-screen-wordmark { font-family: var(--font-mono); font-weight: 700; letter-spacing: .18em; color: var(--live-purple); text-transform: uppercase; }
.live-screen-divider { width: 1px; height: 1.75rem; background: color-mix(in oklch, var(--primary) 18%, transparent); }
.live-screen-metrics { margin-left: auto; display: grid; grid-auto-flow: column; gap: 1.75rem; }
.live-screen-metrics div { min-width: 4rem; text-align: center; }
.live-screen-metrics dt { font-size: .625rem; text-transform: uppercase; letter-spacing: .18em; color: var(--primary); }
.live-screen-metrics dd { margin-top: .2rem; font-family: var(--font-mono); font-size: 1.4rem; font-weight: 700; color: var(--foreground); }
.live-screen-metrics dd span { margin-left: .15rem; font-size: .7rem; color: var(--primary); }
.live-screen-countdown { color: var(--live-green); text-shadow:none; }

.live-screen-layout { position: relative; z-index: 1; display: grid; grid-template-columns: minmax(0, 1fr) clamp(16rem, 23vw, 32rem); min-height: 0; min-width: 0; gap: .75rem; padding: .75rem; }
.live-arena {
  position: relative;
  min-height: 0;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--live-line);
  background: var(--background);
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
  background: repeating-linear-gradient(180deg, color-mix(in oklch, var(--foreground) 2%, transparent) 0 1px, transparent 1px 4px);
}
.live-arena-vignette {
  position: absolute;
  inset: 0;
  z-index: 4;
  pointer-events: none;
  background: radial-gradient(ellipse at center, transparent 52%, color-mix(in oklch, var(--background) 55%, transparent) 100%);
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
  color: var(--primary);
  pointer-events: none;
}
.live-arena-pulse { width: .5rem; height: .5rem; border-radius: 999px; background: var(--live-green); box-shadow:var(--panel-shadow); animation: live-dot-pulse 1.6s ease-in-out infinite; }
.live-frozen { color: var(--warning); }
.live-overlay {
  position: absolute;
  inset: 0;
  z-index: 6;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  background: color-mix(in oklch, var(--background) 82%, transparent);
  color: var(--primary);
}
.live-overlay-icon { color: var(--live-purple); }
.live-corners i { position: absolute; z-index: 5; width: 1.4rem; height: 1.4rem; border-color: color-mix(in oklch, var(--primary) 70%, transparent); }
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
  border: 1px solid color-mix(in oklch, var(--primary) 50%, transparent);
  background: linear-gradient(180deg, color-mix(in oklch, var(--background) 94%, transparent), color-mix(in oklch, var(--background) 84%, transparent));
  box-shadow:var(--panel-shadow);
  clip-path: polygon(0 0, calc(100% - .55rem) 0, 100% .55rem, 100% 100%, .55rem 100%, 0 calc(100% - .55rem));
  text-align: center;
  transition: opacity .5s ease, filter .5s ease, border-color .5s ease, box-shadow .5s ease;
}
.live-arena :deep(.live-label[data-state="solved"]) {
  border-color: color-mix(in oklch, var(--destructive) 60%, transparent);
  box-shadow:var(--panel-shadow);
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
  color: var(--foreground);
}
.live-arena :deep(.live-label-meta) { display: flex; align-items: baseline; gap: .5rem; }
.live-arena :deep(.live-label-pts) { font-family: var(--font-mono); font-size: .78rem; font-weight: 700; color: var(--live-blue); text-shadow:none; }
.live-arena :deep(.live-label[data-state="solved"] .live-label-pts) { color: var(--live-red); text-shadow:none; }
.live-arena :deep(.live-label-solves) { font-family: var(--font-mono); font-size: .62rem; color: var(--primary); }
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
.live-arena :deep(.live-label-blood-first) { color: var(--warning); text-shadow:none; }
.live-arena :deep(.live-label-blood-second) { color: var(--foreground); }
.live-arena :deep(.live-label-blood-third) { color: var(--destructive); }

/* Dense/narrow overviews keep names and points; a focused challenge retains its full label. */
.live-arena[data-compact-labels="true"] :deep(.live-label:not([data-focus="true"])) {
  min-width: 0;
  width: var(--live-label-width);
  max-width: var(--live-label-width);
  padding: .25rem .35rem;
}
.live-arena[data-compact-labels="true"] :deep(.live-label:not([data-focus="true"]) .live-label-name) { font-size: .7rem; }
.live-arena[data-compact-labels="true"] :deep(.live-label:not([data-focus="true"]) .live-label-pts) { font-size: .65rem; }
.live-arena[data-compact-labels="true"] :deep(.live-label:not([data-focus="true"]) .live-label-solves),
.live-arena[data-compact-labels="true"] :deep(.live-label:not([data-focus="true"]) .live-label-bloods) { display: none; }

/* 解题聚焦横幅。 */
.live-celebration { position: absolute; z-index: 9; inset: 0; display: grid; place-items: center; pointer-events: none; }
.live-celebration-flash { position: absolute; inset: 0; background: radial-gradient(circle at center, color-mix(in oklch, var(--success) 30%, transparent), transparent 48%); animation: live-flash 1.15s ease-out both; }
.live-celebration-impact { position: absolute; left: 50%; top: 59%; width: 1px; height: 1px; }
.live-impact-ring { position: absolute; left: 0; top: 0; width: 5rem; aspect-ratio: 1; border: 2px solid var(--live-green); border-radius: 50%; transform: translate(-50%, -50%) scale(.12); box-shadow:var(--panel-shadow); animation: live-impact-ring 1.5s cubic-bezier(.16, 1, .3, 1) both; }
.live-impact-ring-two { animation-delay: .18s; border-color: var(--live-purple); }
.live-impact-ring-three { animation-delay: .36s; }
.live-celebration-particle { --particle-angle: 0deg; --particle-distance: 12rem; --particle-delay: 0ms; position: absolute; left: 0; top: 0; width: .35rem; height: .35rem; background: var(--live-green); box-shadow:var(--panel-shadow); animation: live-particle 1.25s cubic-bezier(.16, 1, .3, 1) var(--particle-delay) both; }
.live-celebration-particle:nth-of-type(3n) { width: .24rem; height: .7rem; background: var(--live-purple); box-shadow:var(--panel-shadow); }
.live-celebration-card { position: relative; display: flex; min-width: min(31rem, 74%); flex-direction: column; align-items: center; border: 1px solid color-mix(in oklch, var(--primary) 55%, transparent); background: color-mix(in oklch, var(--background) 94%, transparent); padding: 1.45rem 3rem 1.25rem; text-align: center; box-shadow:var(--panel-shadow); animation: live-card-arrive 5.2s cubic-bezier(.16, 1, .3, 1) both; }
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
.live-celebration-progress { position: absolute; left: .65rem; right: .65rem; bottom: .42rem; height: 2px; background: color-mix(in oklch, var(--foreground) 6%, transparent); overflow: hidden; }
.live-celebration-progress i { display: block; height: 100%; background: var(--live-green); transform-origin: left; animation: live-celebration-progress 5.2s linear both; }
.live-blood-solve { color: var(--live-green); }
.live-blood-first { color: var(--warning); }
.live-blood-second { color: var(--foreground); }
.live-blood-third { color: var(--destructive); }

/* 右侧面板。 */
.live-rail { display: flex; min-height: 0; min-width: 0; flex-direction: column; gap: .75rem; }
.live-panel { display: flex; flex-direction: column; overflow: hidden; border: 1px solid var(--live-line); background: var(--live-panel); }
.live-panel-heading { display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid color-mix(in oklch, var(--primary) 12%, transparent); padding: .75rem 1rem; font-size: .7rem; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; }
.live-panel-heading span { font-family: var(--font-mono); font-size: .55rem; color: var(--primary); }
.live-rank-viewport { min-height: 0; flex: 1; overflow: hidden; }
.live-rank-track-scroll { animation: live-rank-scroll linear infinite; }
.live-ranking { padding: .4rem; }
.live-ranking li { display: flex; min-height: 2.55rem; align-items: center; gap: .7rem; border-bottom: 1px solid color-mix(in oklch, var(--primary) 8%, transparent); padding: .35rem .55rem; font-size: .75rem; }
.live-ranking li:last-child { border-bottom: 0; }
.live-ranking strong { min-width: 4.5rem; text-align: right; color: var(--live-green); }
.live-rank { width: 1.5rem; font-family: var(--font-mono); font-weight: 700; color: var(--primary); }
.live-rank-first { background: linear-gradient(90deg, color-mix(in oklch, var(--warning) 13%, transparent), transparent); }
.live-rank-first .live-rank { color: var(--warning); }
.live-rank-second .live-rank { color: var(--foreground); }
.live-rank-third .live-rank { color: var(--destructive); }
.live-feed { min-height: 0; overflow: auto; padding: .4rem; }
.live-feed li { display: grid; grid-template-columns: 3.7rem auto minmax(0, 1fr); align-items: start; gap: .5rem; border-bottom: 1px solid color-mix(in oklch, var(--primary) 8%, transparent); padding: .55rem .35rem; }
.live-feed time { padding-top: .12rem; font-family: var(--font-mono); font-size: .55rem; color: var(--primary); }
.live-feed > li > span { border: 1px solid currentColor; border-radius: 999px; padding: .06rem .32rem; font-size: .52rem; font-weight: 700; white-space: nowrap; }
.live-feed p { font-size: .66rem; line-height: 1.35; color: var(--primary); }
.live-feed strong, .live-feed b { color: var(--foreground); font-style: normal; }
.live-feed em { color: var(--live-green); font-family: var(--font-mono); font-style: normal; font-weight: 700; }
.live-empty { display: flex; align-items: center; justify-content: center; color: var(--primary); font-size: .75rem; }

.live-celebration-enter-active, .live-celebration-leave-active { transition: opacity .42s cubic-bezier(.16, 1, .3, 1); }
.live-celebration-enter-from, .live-celebration-leave-to { opacity: 0; }

@keyframes live-dot-pulse { 0%, 100% { opacity: 1; } 50% { opacity: .35; } }
@keyframes live-rank-scroll { from { transform: translateY(0); } to { transform: translateY(-50%); } }
@keyframes live-flash { 0% { opacity: 0; } 16% { opacity: 1; } 100% { opacity: 0; } }
@keyframes live-impact-ring { 0% { opacity: 0; transform: translate(-50%, -50%) scale(.12); } 14% { opacity: 1; } 100% { opacity: 0; transform: translate(-50%, -50%) scale(5.4); } }
@keyframes live-particle { 0% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(1rem) scale(.5); } 18% { opacity: 1; } 100% { opacity: 0; transform: rotate(var(--particle-angle)) translateX(var(--particle-distance)) scale(1); } }
@keyframes live-card-arrive { 0% { opacity: 0; transform: translateY(-1rem) scale(.84); clip-path: inset(0 50%); } 12% { opacity: 1; transform: translateY(0) scale(1.025); clip-path: inset(0); } 18%, 100% { opacity: 1; transform: scale(1); clip-path: inset(0); } }
@keyframes live-celebration-progress { from { transform: scaleX(1); } to { transform: scaleX(0); } }

@media (max-width: 1280px) {
  .live-screen-header { flex-wrap: wrap; gap: .75rem; padding: .75rem 1rem; }
  .live-screen-metrics { order: 3; width: 100%; justify-content: space-around; border-top: 1px solid color-mix(in oklch, var(--primary) 10%, transparent); padding-top: .7rem; }
}
@media (max-width: 900px) {
  .live-screen { height: auto; min-height: 100dvh; overflow: clip; }
  .live-screen-layout { grid-template-columns: minmax(0, 1fr); grid-template-rows: clamp(22rem, 62dvh, 44rem) auto; }
  .live-rail { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .live-panel { height: clamp(16rem, 35dvh, 24rem); }
}
@media (max-width: 600px) {
  .live-screen-title { flex-basis: 100%; }
  .live-screen-controls { margin-left: auto; }
  .live-rail { grid-template-columns: minmax(0, 1fr); }
  .live-arena-heading { inset: .75rem; flex-wrap: wrap; gap: .3rem; }
  .live-celebration-card { min-width: 0; max-width: 90%; padding: 1rem; }
}
@media (min-width: 2560px) and (min-height: 1200px) {
  .live-screen-header { min-height: 7rem; }
  .live-screen-title h1 { font-size: 2rem; }
  .live-screen-metrics { gap: 2.5rem; }
  .live-screen-metrics dt, .live-screen-footer { font-size: .875rem; }
  .live-screen-metrics dd { font-size: 2rem; }
  .live-arena-heading, .live-panel-heading, .live-ranking li { font-size: 1rem; }
  .live-ranking li { min-height: 3.25rem; }
  .live-feed p { font-size: .9rem; }
  .live-feed time, .live-feed > li > span { font-size: .75rem; }
  .live-feed li { grid-template-columns: 5rem auto minmax(0, 1fr); }
  .live-arena :deep(.live-label) { min-width: 11rem; max-width: 18rem; padding: .6rem .9rem; }
  .live-arena :deep(.live-label-name) { font-size: 1rem; }
  .live-arena :deep(.live-label-pts) { font-size: 1.1rem; }
  .live-arena :deep(.live-label-solves) { font-size: .875rem; }
  .live-arena :deep(.live-label-blood) { max-width: 16rem; font-size: .75rem; }
}
@media (prefers-reduced-motion: reduce) {
  .live-arena-pulse, .live-rank-track-scroll, .live-celebration-flash, .live-impact-ring, .live-celebration-particle, .live-celebration-card, .live-celebration-progress i { animation: none; }
  .live-celebration-enter-active, .live-celebration-leave-active { transition: none; }
  .live-celebration-card { opacity: 1; }
}
</style>
