<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdAwdpLivePageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdAwdpLivePage'

const viewProps = defineProps<{ state: CompetitionsByIdAwdpLivePageViewState }>()
const { Activity, ChevronLeft, ChevronRight, Clock3, Expand, Minimize, Radio, RefreshCw, ShieldCheck, Swords, Trophy, Users, X, directionIcon, scoreboardRankingStateLabel, competitionId, configuration, t, board, competition, events, loading, refreshing, projectionPending, error, activeEvent, playbackQueue, playbackProgress, clock, fullscreen, selectedTeamIndex, teamPanelHovered, teamPanelFocused, teamCarouselPaused, rankedEntries, topEntries, selectedTeam, selectedChallengeStates, recentFeed, tickerEvents, canvasStyle, currentRound, settledRound, operationMetrics, selectedTeamMetrics, remainingText, rankTone, eventLabel, eventTime, isChallengeFocused, refreshLatest, selectPreviousTeam, selectNextTeam, toggleFullscreen, AwdpEventStage, AwdpEventTicker, onMouseenterTeamPanelHovered, onMouseleaveTeamPanelHovered, onFocusinTeamPanelFocused, onFocusoutTeamPanelFocused, onClickSelectedTeamIndex } = toRefs(viewProps.state)
</script>

<template>
  <div class="awdp-control-shell dark">
    <div class="awdp-control-canvas" :style="canvasStyle">
      <div class="awdp-grid" aria-hidden="true" />
      <header class="awdp-topbar hud-panel">
        <div class="round-block">
          <span>{{ $t('ui.currentRound') }}</span><strong>{{ currentRound ? `${$t('ui.round')} ${currentRound} ${$t('ui.message3')}` : $t('ui.symbol') }}</strong>
          <small>{{ $t('ui.settledThroughRound', { round: settledRound }) }}</small>
        </div>
        <div class="title-block">
          <span>{{ configuration?.name ?? $t('ui.noctf') }}</span>
          <h1>{{ competition?.title ?? $t('ui.awdpLiveControlScreen') }}</h1>
          <small>{{ $t('ui.attackDefensePatchSettlement') }}</small>
        </div>
        <dl class="top-stats">
          <div><Users /><dt>{{ $t('ui.team') }}</dt><dd>{{ rankedEntries.length }}</dd></div>
          <div><Activity /><dt>{{ $t('ui.challenge') }}</dt><dd>{{ board.catalog.value?.items?.length ?? 0 }}</dd></div>
          <div class="operation-stat attack"><Swords /><dt>{{ $t('ui.attackSuccessAttempts') }}</dt><dd>{{ operationMetrics.attack.success }} / {{ operationMetrics.attack.total }}</dd></div>
          <div class="operation-stat defense"><ShieldCheck /><dt>{{ $t('ui.defenseSuccessAttempts') }}</dt><dd>{{ operationMetrics.defense.success }} / {{ operationMetrics.defense.total }}</dd></div>
        </dl>
        <div class="clock-block">
          <Clock3 /><span>{{ $t('ui.roundRemaining') }}</span><strong>{{ remainingText }}</strong>
          <div class="screen-actions">
            <ActionButton :aria-label="$t('ui.refreshControlScreen')" @click="refreshLatest"><RefreshCw :class="refreshing && 'spin'" /></ActionButton>
            <ActionButton :aria-label="fullscreen ? $t('ui.exitFullscreen') : $t('ui.enterFullscreen')" @click="toggleFullscreen">
              <Minimize v-if="fullscreen" /><Expand v-else />
            </ActionButton>
            <NuxtLink :to="`/competitions/${competitionId}/leaderboard`" :aria-label="$t('ui.exitControlScreen')"><X /></NuxtLink>
          </div>
        </div>
      </header>

      <main class="awdp-main-grid">
        <section class="event-feed hud-panel">
          <header class="hud-heading">
            <div class="hud-title"><Radio class="hud-title-icon" /><span>{{ $t('ui.teamActivityStream') }}</span></div>
            <b><i /> {{ $t('ui.live') }}</b>
          </header>
          <ol v-if="recentFeed.length" class="feed-list">
            <li v-for="event in recentFeed" :key="event.id" :class="[event.action, event.outcome]">
              <time>{{ eventTime(event.occurredAt) }}</time>
              <span class="feed-symbol">{{ event.action === 'attack' ? '⚔' : '⬡' }}</span>
              <p><strong>{{ event.teamName }}</strong>{{ eventLabel(event) }}<b>{{ event.challengeTitle }}</b></p>
              <em>{{ eventLabel(event) }}</em>
            </li>
          </ol>
          <div v-else class="panel-empty">{{ $t('ui.awaitingCompetitionActivity') }}</div>
          <footer>{{ $t('ui.showsTeamAttackAndDefenseOperationsAgainstChallengesOnly') }}</footer>
        </section>

        <component :is="AwdpEventStage"
          :event="activeEvent"
          :queue-length="playbackQueue.length"
          :progress="playbackProgress"
        />

        <aside class="right-column">
          <section class="ranking-panel hud-panel">
            <header class="hud-heading">
              <div class="hud-title"><Trophy class="hud-title-icon" /><span>{{ $t('ui.liveLeaderboard') }}</span></div>
              <b>{{ $t('ui.settled') }}</b>
            </header>
            <div class="rank-head"><span>{{ $t('ui.ranking2') }}</span><span>{{ $t('ui.team') }}</span><span>{{ $t('ui.attackScore') }}</span><span>{{ $t('ui.defenseScore') }}</span><span>{{ $t('ui.totalScore') }}</span></div>
            <ol v-if="topEntries.length" class="rank-list">
              <li v-for="entry in topEntries" :key="entry.teamId" :class="rankTone(entry.rank)">
                <span class="rank-number">{{ entry.rank ?? $t('ui.symbol') }}</span>
                <strong>{{ entry.teamName }}<small v-if="entry.rankingState !== 'Eligible'"> · {{ scoreboardRankingStateLabel(entry.rankingState) }}</small></strong>
                <span>{{ entry.attackScore }}</span><span>{{ entry.defenseScore }}</span><b>{{ entry.totalScore ?? 0 }}</b>
                <i :class="entry.trend">{{ entry.trend === 'up' ? '↗' : entry.trend === 'down' ? '↘' : '→' }}</i>
              </li>
            </ol>
            <div v-else class="panel-empty">{{ projectionPending ? $t('ui.buildingTheScoreboardProjection') : $t('ui.noTeamHasScoredYet') }}</div>
            <footer>{{ $t('ui.onlyCompletedRoundSettlementsAreShown') }} · {{ eventTime(board.snapshot.value?.generatedAt) }}</footer>
          </section>

          <section
            class="team-panel hud-panel"
            @mouseenter="onMouseenterTeamPanelHovered(true)"
            @mouseleave="onMouseleaveTeamPanelHovered(false)"
            @focusin="onFocusinTeamPanelFocused(true)"
            @focusout="onFocusoutTeamPanelFocused(false)"
          >
            <header class="hud-heading">
              <div class="hud-title"><Activity class="hud-title-icon" /><span>{{ $t('ui.teamChallengeActivity') }}</span></div>
              <span>{{ teamCarouselPaused ? $t('ui.carouselPaused') : $t('ui.rotatesEvery8Seconds') }}</span>
            </header>
            <div v-if="selectedTeam" class="team-carousel">
              <nav class="team-tabs" :aria-label="$t('ui.selectTeam')">
                <ActionButton
                  class="team-tab-control"
                  :aria-label="$t('ui.previousPage')"
                  :disabled="rankedEntries.length <= 1"
                  @click="selectPreviousTeam"
                ><ChevronLeft /></ActionButton>
                <div class="team-tab-list">
                  <ActionButton
                    v-for="(entry, index) in rankedEntries.slice(0, 5)"
                    :key="entry.teamId"
                    :class="selectedTeam?.teamId === entry.teamId && 'active'"
                    @click="onClickSelectedTeamIndex(index)"
                  >{{ entry.teamName }}</ActionButton>
                </div>
                <ActionButton
                  class="team-tab-control"
                  :aria-label="$t('ui.nextPage')"
                  :disabled="rankedEntries.length <= 1"
                  @click="selectNextTeam"
                ><ChevronRight /></ActionButton>
              </nav>
              <div class="team-summary">
                <strong>{{ selectedTeam.teamName }}</strong>
                <span>{{ $t('ui.attack') }} {{ selectedTeamMetrics.attack.success }}/{{ selectedTeamMetrics.attack.total }} · {{ $t('ui.defense') }} {{ selectedTeamMetrics.defense.success }}/{{ selectedTeamMetrics.defense.total }}</span>
              </div>
              <div
                :key="selectedTeam.teamId"
                class="challenge-strip"
                role="region"
                :aria-label="$t('ui.teamChallengeActivity')"
                tabindex="0"
              >
                <article
                  v-for="challenge in selectedChallengeStates"
                  :key="challenge.competitionChallengeId"
                  :class="isChallengeFocused(challenge.competitionChallengeId) && 'event-focus'"
                >
                  <div class="challenge-identity">
                    <span class="challenge-icon"><component :is="directionIcon(challenge.direction)" /></span>
                    <div><span>{{ challenge.direction }}</span><strong :title="challenge.title">{{ challenge.title }}</strong></div>
                  </div>
                  <dl>
                    <div><dt>{{ $t('ui.attack') }}</dt><dd :class="challenge.attackOutcome">{{ challenge.attackOutcome === 'idle' ? $t('ui.symbol') : challenge.attackOutcome === 'pending' ? $t('ui.running') : challenge.attackOutcome === 'success' ? $t('ui.success') : $t('ui.failed') }}</dd></div>
                    <div><dt>{{ $t('ui.defense') }}</dt><dd :class="challenge.defenseOutcome">{{ challenge.defenseOutcome === 'idle' ? $t('ui.symbol') : challenge.defenseOutcome === 'pending' ? $t('ui.verifying') : challenge.defenseOutcome === 'success' ? $t('ui.success') : $t('ui.failed') }}</dd></div>
                  </dl>
                  <footer><span>{{ $t('ui.attackScore') }} {{ challenge.attackScore }}</span><span>{{ $t('ui.defenseScore') }} {{ challenge.defenseScore }}</span></footer>
                </article>
              </div>
            </div>
            <div v-else class="panel-empty">{{ $t('ui.noTeamData') }}</div>
          </section>
        </aside>
      </main>

      <component :is="AwdpEventTicker" :events="tickerEvents" />

      <div v-if="loading || error" class="screen-overlay">
        <Radio v-if="loading" class="pulse" />
        <ShieldCheck v-else />
        <strong>{{ loading ? $t('ui.connectingToLiveCompetitionData') : error }}</strong>
        <ActionButton v-if="error" @click="refreshLatest">{{ $t('ui.reload') }}</ActionButton>
      </div>
    </div>
  </div>
</template>

<style>
:root{--awdp-display:"Arial Narrow","Microsoft YaHei UI",sans-serif;--awdp-mono:"Cascadia Mono","JetBrains Mono",monospace}.awdp-control-shell{position:fixed;inset:0;overflow:hidden;background:#010307;color:#d7f3ff;font-family:var(--awdp-display)}.awdp-control-shell *{box-sizing:border-box}.awdp-control-canvas{position:absolute;left:50%;top:50%;width:1920px;height:1080px;transform-origin:center;overflow:hidden;padding:12px;display:grid;grid-template-rows:96px minmax(0,1fr) 112px;gap:10px;background:radial-gradient(circle at 50% 42%,#041420 0,#01060c 46%,#010205 82%)}.awdp-grid{position:absolute;inset:0;pointer-events:none;background-image:linear-gradient(rgba(0,155,220,.025) 1px,transparent 1px),linear-gradient(90deg,rgba(0,155,220,.025) 1px,transparent 1px);background-size:24px 24px;mask-image:linear-gradient(90deg,#000,transparent 48%,#000)}.hud-panel{position:relative;border:1px solid rgba(0,172,238,.42);background:linear-gradient(180deg,rgba(3,16,26,.96),rgba(1,8,14,.96));box-shadow:inset 0 0 35px rgba(0,106,161,.055),0 0 16px rgba(0,124,190,.07);clip-path:polygon(12px 0,calc(100% - 12px) 0,100% 12px,100% calc(100% - 12px),calc(100% - 12px) 100%,12px 100%,0 calc(100% - 12px),0 12px)}.hud-panel:before,.hud-panel:after{content:"";position:absolute;z-index:3;top:0;width:72px;height:2px;background:#00bffc;box-shadow:0 0 10px #00bffc}.hud-panel:before{left:14px}.hud-panel:after{right:14px}.hud-heading{position:relative;z-index:2;height:48px;display:flex;align-items:center;justify-content:space-between;padding:0 16px;border-bottom:1px solid rgba(0,155,216,.22)}.hud-title{display:flex;align-items:center;gap:9px;font:800 15px var(--awdp-display);letter-spacing:.08em;color:#c9efff}.hud-title-icon{width:18px;height:18px;color:#00bfff}.hud-heading>b,.hud-heading>span{font:700 10px var(--awdp-mono);letter-spacing:.12em;color:#64879a}.awdp-topbar{display:grid;grid-template-columns:250px minmax(370px,1fr) auto 285px;align-items:stretch;padding:0 22px}.round-block,.title-block,.clock-block{display:flex;flex-direction:column;justify-content:center}.round-block{padding-left:15px;border-right:1px solid rgba(0,163,225,.22)}.round-block>span,.clock-block>span{font:700 10px var(--awdp-mono);letter-spacing:.18em;color:#63879a}.round-block>strong{font:900 25px var(--awdp-display);letter-spacing:.08em;color:#d8f5ff}.round-block>small{font:600 10px var(--awdp-mono);color:#497084}.title-block{text-align:center;padding:0 32px}.title-block>span{font:700 10px var(--awdp-mono);letter-spacing:.3em;color:#0ed0ff}.title-block h1{margin:1px 0 0;font:900 30px var(--awdp-display);letter-spacing:.08em;text-shadow:0 0 18px rgba(0,174,255,.25)}.title-block small{font:600 9px var(--awdp-mono);letter-spacing:.22em;color:#54788d}.top-stats{display:flex;align-items:center;gap:18px;margin:0 18px}.top-stats>div{display:grid;grid-template-columns:18px auto;column-gap:6px;min-width:78px}.top-stats svg{grid-row:1/3;width:16px;height:16px;color:#20c8f8;align-self:center}.top-stats dt{font:600 9px var(--awdp-mono);letter-spacing:.08em;color:#5f8295}.top-stats dd{margin:0;font:800 16px var(--awdp-mono);color:#cbeeff}.clock-block{position:relative;padding-left:42px;border-left:1px solid rgba(0,163,225,.22)}.clock-block>svg{position:absolute;left:15px;top:31px;width:20px}.clock-block>strong{font:800 24px var(--awdp-mono);letter-spacing:.1em;color:#22cdfc}.screen-actions{position:absolute;right:0;top:10px;display:flex;gap:4px}.screen-actions button,.screen-actions a{width:29px;height:29px;display:grid;place-items:center;border:1px solid rgba(0,178,235,.17);background:rgba(0,20,31,.7);color:#567a8d}.screen-actions svg{width:14px;height:14px}.screen-actions button:hover,.screen-actions a:hover{color:#e2f8ff;border-color:#00bff8}.spin{animation:spin .8s linear infinite}.awdp-main-grid{display:grid;grid-template-columns:390px minmax(0,1fr) 570px;gap:10px;min-height:0}.event-feed{display:grid;grid-template-rows:48px minmax(0,1fr) 39px;min-height:0}.event-feed .hud-heading b{display:flex;align-items:center;gap:6px;color:#ff3838}.event-feed .hud-heading b i{width:7px;height:7px;border-radius:50%;background:#ff2535;box-shadow:0 0 8px #ff2535}.feed-list{margin:0;padding:5px 11px;list-style:none;overflow:hidden}.feed-list li{height:48px;display:grid;grid-template-columns:53px 24px minmax(0,1fr) auto;align-items:center;gap:7px;border-bottom:1px solid rgba(74,131,158,.13);font:650 11px var(--awdp-mono)}.feed-list time{color:#557488}.feed-symbol{font-size:18px;color:var(--tone)}.feed-list p{min-width:0;margin:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:#90aebd}.feed-list p strong{margin-right:6px;color:#e2f6ff}.feed-list p b{margin-left:6px;color:#77bbd5}.feed-list em{padding:4px 7px;border:1px solid color-mix(in srgb,var(--tone) 52%,transparent);border-radius:3px;color:var(--tone);font-style:normal;font-size:9px}.feed-list li.attack{--tone:#ff4338}.feed-list li.defense{--tone:#00d8c3}.feed-list li.failure{--tone:#ff801c}.feed-list li.pending{--tone:#8c68ff}.event-feed>footer{display:grid;place-items:center;border-top:1px solid rgba(0,155,216,.17);font:600 10px var(--awdp-mono);letter-spacing:.1em;color:#406c80}.right-column{display:grid;grid-template-rows:330px minmax(0,1fr);gap:10px;min-height:0}.ranking-panel{display:grid;grid-template-rows:48px 28px minmax(0,1fr) 28px;min-height:0}.rank-head,.rank-list li{display:grid;grid-template-columns:42px minmax(0,1fr) 73px 73px 76px 22px;align-items:center;column-gap:5px}.rank-head{padding:0 12px;font:650 9px var(--awdp-mono);letter-spacing:.1em;color:#55778a}.rank-head span:nth-child(n+3){text-align:right}.rank-list{margin:0;padding:0 11px;list-style:none;overflow:hidden}.rank-list li{height:27px;padding:0 3px;border-bottom:1px solid rgba(65,129,158,.12);font:650 11px var(--awdp-mono);color:#829eac}.rank-list li strong{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:#c7e7f3}.rank-list li>span:nth-child(n+3),.rank-list li>b{text-align:right;font-variant-numeric:tabular-nums}.rank-number{width:22px;height:20px;display:grid;place-items:center;border:1px solid rgba(72,130,155,.28);font-weight:800}.rank-list li.gold{color:#ffc640}.rank-list li.silver{color:#d5e2ec}.rank-list li.bronze{color:#de8c4c}.rank-list li i{text-align:right;font-style:normal}.rank-list li i.up{color:#20e89f}.rank-list li i.down{color:#ff4b3e}.ranking-panel>footer{display:flex;align-items:center;justify-content:space-between;padding:0 13px;border-top:1px solid rgba(0,155,216,.14);font:600 9px var(--awdp-mono);color:#496d7e}.team-panel{display:grid;grid-template-rows:48px minmax(0,1fr);min-height:0}.team-carousel{display:grid;grid-template-rows:36px 45px minmax(0,1fr);min-height:0;padding:7px 10px 10px}.team-tabs{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:4px}.team-tabs button{min-width:0;border:1px solid rgba(0,151,213,.25);background:rgba(1,12,20,.8);color:#67889b;font:650 9px var(--awdp-mono);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.team-tabs button:first-child,.team-tabs button:last-child{width:30px}.team-tabs button.active{border-color:#00c7ff;color:#dcf7ff;background:rgba(0,135,194,.18);box-shadow:inset 0 -2px #00c7ff}.team-tabs svg{width:13px}.team-summary{display:flex;align-items:center;justify-content:space-between;padding:0 7px}.team-summary strong{font:800 18px var(--awdp-display);letter-spacing:.04em}.team-summary span{font:650 10px var(--awdp-mono);color:#5e8799}.challenge-strip{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:7px;min-height:0}.challenge-strip article{position:relative;min-width:0;padding:10px 8px 24px;border:1px solid rgba(0,160,220,.34);background:linear-gradient(180deg,rgba(0,83,118,.12),rgba(2,8,14,.94));clip-path:polygon(8px 0,100% 0,100% calc(100% - 8px),calc(100% - 8px) 100%,0 100%,0 8px)}.challenge-strip article>span{font:700 8px var(--awdp-mono);letter-spacing:.14em;color:#3aaad1}.challenge-strip article>strong{display:block;margin-top:6px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font:800 12px var(--awdp-display)}.challenge-strip dl{margin:13px 0 0;display:grid;gap:6px}.challenge-strip dl div{display:flex;justify-content:space-between;font:650 9px var(--awdp-mono);color:#607f90}.challenge-strip dd{margin:0}.challenge-strip dd.success{color:#1ce5a7}.challenge-strip dd.failure{color:#ff6948}.challenge-strip dd.pending{color:#a688ff}.challenge-strip article footer{position:absolute;left:8px;right:8px;bottom:7px;text-align:right;font:700 8px var(--awdp-mono);color:#416b7e}.panel-empty{display:grid;place-items:center;font:650 11px var(--awdp-mono);letter-spacing:.12em;color:#466b7e}.screen-overlay{position:absolute;z-index:50;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:18px;background:rgba(0,4,8,.92);backdrop-filter:blur(6px)}.screen-overlay svg{width:46px;height:46px;color:#00c9ff}.screen-overlay strong{font:750 15px var(--awdp-mono);letter-spacing:.12em;color:#9fc4d5}.screen-overlay button{border:1px solid #00bce9;background:rgba(0,139,191,.12);padding:10px 20px;color:#bdeeff}.pulse{animation:pulse 1.3s ease-in-out infinite}@keyframes spin{to{rotate:360deg}}@keyframes pulse{50%{opacity:.3;scale:.88}}@media(prefers-reduced-motion:reduce){.awdp-control-shell *{animation-duration:.001ms!important;animation-iteration-count:1!important}}
</style>

<style>
.awdp-control-shell::before{content:"";position:absolute;inset:-35%;pointer-events:none;background:conic-gradient(from 30deg at 50% 50%,transparent 0 18%,rgba(0,190,255,.09) 21%,transparent 25% 51%,rgba(255,42,34,.08) 54%,transparent 59% 100%);filter:blur(22px);animation:awdp-energy-orbit 22s linear infinite}.awdp-control-shell::after{content:"";position:absolute;inset:0;pointer-events:none;z-index:20;opacity:.2;background:repeating-linear-gradient(180deg,transparent 0 3px,rgba(76,203,255,.035) 4px);mix-blend-mode:screen}.awdp-control-canvas::before{content:"";position:absolute;z-index:0;left:50%;top:48%;width:1120px;height:720px;translate:-50% -50%;border-radius:50%;pointer-events:none;background:radial-gradient(circle,transparent 0 27%,rgba(0,196,255,.055) 28% 28.4%,transparent 29% 43%,rgba(255,49,40,.035) 44% 44.4%,transparent 45%);filter:drop-shadow(0 0 34px rgba(0,190,255,.16));animation:awdp-core-breathe 4.8s ease-in-out infinite}.awdp-topbar,.awdp-main-grid,.awdp-control-canvas>AwdpEventTicker{position:relative;z-index:2}.top-stats .operation-stat{min-width:132px}.top-stats .operation-stat.attack svg,.top-stats .operation-stat.attack dd{color:#ff5148;text-shadow:0 0 12px rgba(255,42,33,.5)}.top-stats .operation-stat.defense svg,.top-stats .operation-stat.defense dd{color:#16e6ce;text-shadow:0 0 12px rgba(0,226,200,.45)}.top-stats .operation-stat dt{white-space:nowrap}.team-panel{box-shadow:inset 0 0 44px rgba(0,160,225,.06),0 0 28px rgba(0,169,235,.08)}.challenge-strip article{isolation:isolate;overflow:hidden;transition:border-color .25s ease,box-shadow .25s ease,filter .25s ease}.challenge-strip article::before{content:"";position:absolute;z-index:-1;inset:-45%;background:conic-gradient(from 0deg,transparent,rgba(0,202,255,.12),transparent 28%,transparent 63%,rgba(255,56,42,.09),transparent 78%);animation:awdp-card-radar 9s linear infinite}.challenge-strip article::after{content:"";position:absolute;left:-60%;right:-60%;top:-4px;height:2px;background:linear-gradient(90deg,transparent,#18d7ff,transparent);box-shadow:0 0 14px #16ccff;animation:awdp-card-scan 4.5s ease-in-out infinite}.challenge-identity{display:grid;grid-template-columns:42px minmax(0,1fr);align-items:center;gap:8px}.challenge-identity>div{min-width:0}.challenge-identity>div>span{font:700 8px var(--awdp-mono);letter-spacing:.14em;color:#3aaad1}.challenge-identity strong{display:block;margin-top:3px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font:800 12px var(--awdp-display)}.challenge-icon{width:40px;height:40px;display:grid;place-items:center;border:1px solid rgba(0,190,246,.42);background:radial-gradient(circle,rgba(0,192,255,.2),rgba(0,41,61,.12) 68%);clip-path:polygon(20% 0,80% 0,100% 20%,100% 80%,80% 100%,20% 100%,0 80%,0 20%);color:#33d5ff;filter:drop-shadow(0 0 8px rgba(0,196,255,.35))}.challenge-icon svg{width:22px;height:22px;stroke-width:1.8}.challenge-strip article footer{display:flex;justify-content:space-between;text-align:left}.challenge-strip article footer span:first-child{color:#ff6258}.challenge-strip article footer span:last-child{color:#21dac9}.challenge-strip article.event-focus{border-color:#fff;box-shadow:inset 0 0 28px rgba(0,204,255,.18),0 0 10px #fff,0 0 34px rgba(0,191,255,.65);animation:awdp-event-focus 1.05s ease-in-out infinite alternate}.challenge-strip article.event-focus .challenge-icon{color:#fff;border-color:#fff;filter:drop-shadow(0 0 12px #fff) drop-shadow(0 0 22px #00c9ff)}.challenge-strip article.event-focus::after{height:4px;background:linear-gradient(90deg,transparent,#fff 45%,#ff493d 55%,transparent);box-shadow:0 0 20px #fff;animation-duration:.8s}.feed-list li.success{background:linear-gradient(90deg,color-mix(in srgb,var(--tone) 8%,transparent),transparent)}.feed-list li.failure{background:linear-gradient(90deg,rgba(255,47,35,.1),transparent)}
@keyframes awdp-energy-orbit{to{rotate:360deg}}@keyframes awdp-core-breathe{50%{scale:1.08;opacity:.72}}@keyframes awdp-card-radar{to{rotate:360deg}}@keyframes awdp-card-scan{0%,12%{translate:0 0;opacity:0}22%{opacity:1}70%,100%{translate:0 220px;opacity:0}}@keyframes awdp-event-focus{from{filter:saturate(1.15) brightness(1)}to{filter:saturate(1.5) brightness(1.24)}}
@media(prefers-reduced-motion:reduce){.awdp-control-shell::before,.awdp-control-canvas::before,.challenge-strip article::before,.challenge-strip article::after,.challenge-strip article.event-focus{animation:none!important}}
</style>

<style>
.team-carousel{grid-template-rows:36px 45px minmax(0,1fr)}
.team-tabs{grid-template-columns:30px minmax(0,1fr) 30px;grid-auto-flow:row;grid-auto-columns:auto}
.team-tab-list{display:flex;min-width:0;gap:4px;overflow:hidden}
.team-tab-list button{flex:1 1 0;width:auto!important}
.team-tab-control{width:30px;display:grid;place-items:center}
.team-tab-control:disabled{cursor:default;opacity:.35}
.challenge-strip {
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 160px), 1fr));
  grid-auto-rows: minmax(150px, max-content);
  align-content: start;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-gutter: stable;
  color-scheme: dark;
}
.challenge-strip:focus-visible { outline: 1px solid currentColor; outline-offset: -1px; }
.challenge-strip .challenge-identity strong {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  white-space: normal;
  overflow-wrap: anywhere;
}
.challenge-strip article{padding-top:8px}
.challenge-strip dl{margin-top:7px;gap:3px}
.challenge-identity{grid-template-columns:34px minmax(0,1fr);gap:7px}
.challenge-icon{width:32px;height:32px}
.challenge-icon svg{width:18px;height:18px}
</style>
