<script setup lang="ts">
import { toRefs } from 'vue'
import type { useSpectatorScreen } from '../../../features/spectator/useSpectatorScreen'
import type { bindViewState } from '../../../features/shared/view-state'
const props = defineProps<{ state: ReturnType<typeof bindViewState<ReturnType<typeof useSpectatorScreen>>> }>()
const { t, mode, transferText, fullscreenActive, fullscreenFailed, diagnostics, metricsText, snapshot, loading, failed, fallback, focus, teamFocus, challengeFocus, minutes, reduced, fps, locked,
  carouselLocked, group, groups, visibleNodes, fallbackRows, timeline, timelineTicks, historyPaused, toggleHistory, rankTeams, teams, challenges, team, services, point, pointTitle,
  stale, clockText, statusText, history, settlements, summaryCount,
  teamName, controlName, challengeName, formatTime, healthText, settlementText, eventText, controlledCount, mountScene, previousTeam, nextTeam,
  previousGroup, nextGroup, toggleReduced, toggleFps, toggleLock, toggleCarousel, enterHover, leaveHover,
  enterFocus, leaveFocus, fullscreen, back, refresh } = toRefs(props.state)
</script>

<template>
  <ScrollSurface data-spectator-screen class="spectator-page-scroll">
  <main class="spectator-screen dark" :data-mode="mode">
    <header class="spectator-header">
      <div class="spectator-brand"><span class="spectator-kicker">{{ t(mode === 'Awd' ? 'spectator.awd' : 'spectator.koh') }}</span><h1>{{ snapshot?.competitionName ?? t('spectator.title') }}</h1></div>
      <div class="spectator-round"><span>{{ statusText }}</span><strong v-if="mode === 'Awd'">{{ t('spectator.round', { number: snapshot?.round?.number ?? '—' }) }}</strong><b>{{ mode === 'Awd' ? clockText : t('spectator.discrete') }}</b></div>
      <div class="spectator-header-actions"><Button variant="ghost" size="sm" @click="back">{{ t('spectator.back') }}</Button><Button variant="ghost" size="sm" @click="fullscreen">{{ t(fullscreenActive ? 'spectator.exitFullscreen' : 'spectator.fullscreen') }}</Button></div>
    </header>
    <div class="spectator-toolbar">
      <Select v-model="focus"><SelectTrigger :aria-label="t('spectator.view')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">{{ t('spectator.overview') }}</SelectItem><SelectItem value="team">{{ t('spectator.teamFocus') }}</SelectItem><SelectItem value="challenge">{{ t('spectator.challengeFocus') }}</SelectItem></SelectContent></Select>
      <Select v-model="teamFocus"><SelectTrigger :aria-label="t('spectator.teamFocus')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">{{ t('spectator.allTeams') }}</SelectItem><SelectItem v-for="item in teams" :key="item.id" :value="item.id!">{{ item.name }}</SelectItem></SelectContent></Select>
      <Select v-model="challengeFocus"><SelectTrigger :aria-label="t('spectator.challengeFocus')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">{{ t('spectator.allChallenges') }}</SelectItem><SelectItem v-for="item in challenges" :key="item.competitionChallengeId" :value="item.competitionChallengeId!">{{ item.title }}</SelectItem></SelectContent></Select>
      <div class="spectator-toolbar-end"><Button variant="ghost" size="sm" :aria-pressed="locked" @click="toggleLock">{{ t(locked ? 'spectator.cameraLocked' : 'spectator.cameraFree') }}</Button><Button variant="ghost" size="sm" :aria-pressed="reduced" @click="toggleReduced">{{ t(reduced ? 'spectator.motionOff' : 'spectator.motionOn') }}</Button><Button variant="ghost" size="sm" @click="toggleFps">{{ t('spectator.fps', { count: fps }) }}</Button></div>
    </div>
    <p v-if="fullscreenFailed" role="status" class="spectator-notice">{{ t('spectator.fullscreenFailed') }}</p>
    <div v-if="stale || loading || !snapshot || snapshot.scope !== 'Live'" class="spectator-notice" role="status">
      {{ loading ? t('spectator.loading') : stale ? t('spectator.stale') : !snapshot ? t('spectator.unavailable') : statusText }}
      <Button v-if="failed" variant="ghost" size="sm" @click="refresh">{{ t('spectator.retry') }}</Button>
    </div>
    <div v-if="snapshot && snapshot.scope !== 'Hidden'" class="spectator-body">
      <section class="spectator-field" :aria-label="t('spectator.field')">
        <div class="spectator-field-heading"><span>{{ t(mode === 'Awd' ? 'spectator.situation' : 'spectator.territory') }}</span><small>{{ t('spectator.group', { current: group + 1, total: groups, count: visibleNodes.length }) }}</small></div>
        <WebglViewport v-if="!fallback" :mount="mountScene" :label="t(mode === 'Awd' ? 'spectator.situation' : 'spectator.territory')" />
        <ScrollSurface v-else class="spectator-fallback"><p>{{ t('spectator.fallback') }}</p><article v-for="node in fallbackRows" :key="node.id"><strong>{{ node.name }}</strong><p v-if="mode === 'Koh'">{{ node.controller }} · {{ healthText(node.health) }}</p><dl v-else><div v-for="service in node.services" :key="service.name"><dt>{{ service.name }}</dt><dd :data-health="service.health">{{ healthText(service.health) }}<span v-if="service.compromised" class="spectator-capture">◆ {{ t('spectator.compromised') }}</span></dd></div></dl></article></ScrollSurface>
        <div class="spectator-field-legend" v-if="mode === 'Awd'"><span><i class="spectator-dot" data-health="Ok" />{{ t('spectator.ok') }}</span><span><i class="spectator-dot" data-health="Mumble" />{{ t('spectator.mumble') }}</span><span><i class="spectator-dot" data-health="Offline" />{{ t('spectator.offline') }}</span><span><i class="spectator-dot" data-health="Recovering" />{{ t('spectator.recovering') }}</span><span><i class="spectator-dot" data-health="InternalError" />{{ t('spectator.internalError') }}</span><span><i class="spectator-dot" data-health="Unknown" />{{ t('spectator.pending') }}</span><span class="spectator-capture">◆ {{ t('spectator.compromised') }}</span></div>
        <div class="spectator-field-legend" v-else><span class="spectator-dot" data-health="Healthy" />{{ t('spectator.confirmed') }}<span class="spectator-capture">◆ {{ t('spectator.controlChange') }}</span><span class="spectator-dot" data-health="Unknown" />{{ t('spectator.unknown') }}</div>
        <div v-if="groups > 1" class="spectator-field-pages"><Button variant="ghost" size="sm" @click="previousGroup">{{ t('spectator.previousGroup') }}</Button><Button variant="ghost" size="sm" @click="nextGroup">{{ t('spectator.nextGroup') }}</Button></div>
        <p v-if="transferText" class="spectator-transfer" role="status">{{ transferText }}</p>
        <p v-if="summaryCount && !fallback" class="spectator-burst">{{ t('spectator.burst', { count: summaryCount }) }}</p>
      </section>
      <aside class="spectator-sidebar">
        <section class="spectator-panel spectator-ranking"><h2><span>{{ t('spectator.ranking') }}</span><small>{{ t('spectator.teamCount', { count: teams.length }) }}</small></h2>
          <ScrollSurface class="spectator-rank-scroll"><ol><li v-for="item in rankTeams" :key="item.id"><span class="spectator-rank">{{ item.rank ?? '—' }}</span><span class="spectator-team-name">{{ item.name }}<small v-if="mode === 'Koh'">{{ t('spectator.controlledCount', { count: controlledCount(item.id) }) }}</small></span><strong>{{ item.score?.toLocaleString() }}</strong></li></ol></ScrollSurface>
        </section>
        <section v-if="mode === 'Awd'" class="spectator-panel spectator-services" @mouseenter="enterHover" @mouseleave="leaveHover" @focusin="enterFocus" @focusout="leaveFocus">
          <h2><span>{{ t('spectator.serviceStatus') }}</span><small>{{ t(carouselLocked || focus === 'team' ? 'spectator.locked' : 'spectator.auto') }}</small></h2>
          <div class="spectator-service-team"><h3>{{ team?.name ?? '—' }}</h3><span>{{ t('spectator.serviceCount', { count: services.length }) }}</span></div>
          <ScrollSurface class="spectator-service-scroll"><div v-for="service in services" :key="service.competitionChallengeId" class="spectator-service-row"><span class="spectator-dot" :data-health="service.state" /><div><strong>{{ service.title }}</strong><small>{{ healthText(service.state) }} · {{ settlementText(service.settlement) }}</small><small v-if="service.checkedAt">{{ formatTime(service.checkedAt) }}</small><small v-else-if="service.lastTrustedAt">{{ t('spectator.previousHealth', { health: healthText(service.lastTrustedHealth), time: formatTime(service.lastTrustedAt) }) }}</small></div><span v-if="service.isCompromised" class="spectator-capture" :aria-label="t('spectator.compromised')">◆<small>{{ t('spectator.compromised') }}</small></span></div></ScrollSurface>
          <div class="spectator-carousel-controls"><Button variant="ghost" size="sm" :disabled="focus === 'team'" @click="previousTeam">{{ t('spectator.previousTeam') }}</Button><Button variant="ghost" size="sm" :aria-pressed="carouselLocked" :disabled="focus === 'team'" @click="toggleCarousel">{{ t(carouselLocked ? 'spectator.unlock' : 'spectator.lock') }}</Button><Button variant="ghost" size="sm" :disabled="focus === 'team'" @click="nextTeam">{{ t('spectator.nextTeam') }}</Button></div>
        </section>
        <section v-else class="spectator-panel spectator-point"><h2>{{ t('spectator.pointStatus') }}</h2><ScrollSurface class="spectator-point-content"><h3>{{ pointTitle ?? '—' }}</h3><p class="spectator-owner">{{ controlName(point?.controllerId, point?.health) }}</p><dl><div><dt>{{ t('spectator.checkState') }}</dt><dd>{{ healthText(point?.health) }}</dd></div><div><dt>{{ t('spectator.checked') }}</dt><dd>{{ formatTime(point?.checkedAt) }}</dd></div><div><dt>{{ t('spectator.rule') }}</dt><dd>{{ t('spectator.pointRule', { points: point?.pointsPerInterval ?? '—', seconds: point?.intervalSeconds ?? '—' }) }}</dd></div><div v-if="point?.observedTeamId !== point?.controllerId"><dt>{{ t('spectator.observed') }}</dt><dd>{{ teamName(point?.observedTeamId) }}</dd></div></dl><p class="spectator-explanation">{{ t('spectator.controlExplanation') }}</p></ScrollSurface></section>
      </aside>
      <section class="spectator-panel spectator-bottom">
        <h2><span>{{ t(mode === 'Awd' ? 'spectator.settlement' : 'spectator.timeline') }}</span><small>{{ formatTime(snapshot.dataAsOf) }}</small><Button v-if="mode === 'Koh'" variant="ghost" size="sm" :aria-pressed="historyPaused" @click="toggleHistory">{{ t(historyPaused ? 'spectator.historyResume' : 'spectator.historyPause') }}</Button><Select v-if="mode === 'Koh'" v-model="minutes"><SelectTrigger :aria-label="t('spectator.window')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="5">{{ t('spectator.minutes', { count: 5 }) }}</SelectItem><SelectItem value="10">{{ t('spectator.minutes', { count: 10 }) }}</SelectItem><SelectItem value="30">{{ t('spectator.minutes', { count: 30 }) }}</SelectItem></SelectContent></Select></h2>
        <div v-if="mode === 'Awd'" class="spectator-settlements"><div v-for="row in settlements" :key="row.roundId"><span>{{ t('spectator.round', { number: row.number ?? '—' }) }} · {{ team?.name }}</span><dl><div><dt>{{ t('spectator.attackGain') }}</dt><dd>+{{ row.attack }}</dd></div><div><dt>{{ t('spectator.attackLoss') }}</dt><dd>−{{ row.lost }}</dd></div><div><dt>{{ t('spectator.serviceScore') }}</dt><dd>{{ row.service }}</dd></div><div><dt>{{ t('spectator.other') }}</dt><dd>{{ row.other }}</dd></div></dl></div><p v-if="!settlements.length">{{ t('spectator.noSettlement') }}</p></div>
        <ScrollSurface v-if="mode === 'Koh'" class="spectator-timeline">
          <div v-for="row in timeline" :key="row.id" class="spectator-timeline-row"><strong>{{ row.name }}</strong><div class="spectator-timeline-track">
            <Hint v-for="segment in row.segments" :key="segment.id" :content="segment.label"><span class="spectator-timeline-segment" :style="segment.style" :data-owner="segment.owner >= 0 ? segment.owner : undefined" :data-unknown="segment.unknown" :aria-label="segment.label" role="img" /></Hint>
            <span v-for="mark in row.marks" :key="mark.id" class="spectator-timeline-mark" :style="mark.style" aria-hidden="true" />
          </div></div><div class="spectator-timeline-axis"><span v-for="tick in timelineTicks" :key="tick">{{ tick }}</span></div>
        </ScrollSurface>
        <ScrollSurface v-else class="spectator-history" axis="x"><div class="spectator-history-items"><article v-for="event in history" :key="event.id"><time>{{ formatTime(event.at) }}</time><strong>{{ eventText(event) }}</strong><span>{{ challengeName(event.challengeId) }}</span></article><p v-if="!history.length">{{ t('spectator.noEvents') }}</p></div></ScrollSurface>
        <p v-if="snapshot.historyTruncated" class="spectator-explanation">{{ t('spectator.truncated') }}</p>
      </section>
    </div>
    <output v-if="diagnostics" data-testid="spectator-metrics">{{ metricsText }}</output>
    <footer class="spectator-footer"><span>{{ t('spectator.publicFeed') }}</span><span>{{ t('spectator.authoritative') }}</span><span>{{ t('spectator.version', { version: snapshot?.version ?? '—' }) }}</span></footer>
  </main>
  </ScrollSurface>
</template>
