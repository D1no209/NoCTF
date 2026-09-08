<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpEventStageViewState } from '~/features/awdp-control/useAwdpEventStage'

const viewProps = defineProps<{ state: AwdpEventStageViewState }>()
const { Activity, Crosshair, RadioTower, Shield, animationComponent, event, queueLength, progress } = toRefs(viewProps.state)
</script>

<template>
  <section class="awdp-stage hud-panel" aria-labelledby="awdp-stage-title">
    <header class="hud-heading stage-heading">
      <div class="hud-title">
        <RadioTower class="hud-title-icon" />
        <span id="awdp-stage-title">{{ $t('ui.centralEventStage') }}</span>
      </div>
      <div class="stage-queue"><i />{{ $t('ui.queue') }} {{ queueLength }}</div>
    </header>

    <div class="stage-viewport" aria-live="assertive">
      <Transition name="stage-swap" mode="out-in">
        <div v-if="event && animationComponent" :key="event.id" class="stage-event">
          <div class="stage-event-copy">
            <span>{{ event.action === 'attack' ? $t('ui.attackVerification') : $t('ui.defenseVerification') }}</span>
            <strong>
              {{ event.teamName }}
              <b :class="event.outcome">{{ event.outcome === 'success' ? $t('ui.success') : $t('ui.failed') }}</b>
            </strong>
            <small>{{ event.challengeTitle }}</small>
          </div>
          <component :is="animationComponent" :event="event" />
          <div class="stage-timeline" aria-hidden="true"><i :style="{ width: `${progress}%` }" /></div>
        </div>

        <div v-else class="stage-idle">
          <div class="idle-orbit orbit-one"><i /></div>
          <div class="idle-orbit orbit-two"><i /></div>
          <div class="idle-core"><Activity /></div>
          <Crosshair class="idle-mark idle-mark-left" />
          <Shield class="idle-mark idle-mark-right" />
          <strong>{{ $t('ui.awaitingVerifiedOperation') }}</strong>
          <span>{{ $t('ui.verifiedAttackAndDefenseResultsPlayHereInEventOrder') }}</span>
        </div>
      </Transition>
      <div class="stage-scanline" aria-hidden="true" />
      <div class="hud-corners" aria-hidden="true"><i /><i /><i /><i /></div>
    </div>

    <footer class="stage-footer">
      <span><i class="live-dot" /> {{ $t('ui.liveVerifiedFeed') }}</span>
      <span>{{ event ? formatDateTime(event.occurredAt) : $t('ui.standingBy') }}</span>
    </footer>
  </section>
</template>

<style scoped>
.awdp-stage{display:grid;grid-template-rows:3.1rem minmax(0,1fr) 2.25rem;min-width:0;min-height:0}.stage-heading{border-bottom-color:rgba(0,179,255,.28)}.stage-queue{display:flex;align-items:center;gap:.5rem;font:650 .72rem var(--awdp-mono);letter-spacing:.14em;color:#7294a9}.stage-queue i{width:.42rem;height:.42rem;border-radius:50%;background:#08cfff;box-shadow:0 0 12px #08cfff}.stage-viewport{position:relative;overflow:hidden;min-height:0;background:radial-gradient(circle at 50% 47%,rgba(0,92,139,.16),transparent 52%),linear-gradient(180deg,rgba(0,8,15,.82),rgba(0,4,9,.96))}.stage-viewport:before{content:"";position:absolute;inset:0;background-image:linear-gradient(rgba(0,181,255,.035) 1px,transparent 1px),linear-gradient(90deg,rgba(0,181,255,.035) 1px,transparent 1px);background-size:2rem 2rem;mask-image:radial-gradient(circle,#000 22%,transparent 78%)}.stage-event{position:absolute;inset:0}.stage-event-copy{position:absolute;z-index:5;top:1.25rem;left:1.5rem;display:grid;gap:.18rem;text-transform:uppercase}.stage-event-copy>span{font:700 .68rem var(--awdp-mono);letter-spacing:.22em;color:#7696a9}.stage-event-copy>strong{font:850 1.55rem var(--awdp-display);letter-spacing:.04em;color:#d9f4ff}.stage-event-copy b{margin-left:.5rem;font-size:1.05rem}.stage-event-copy b.success{color:#27efb6}.stage-event-copy b.failure{color:#ff4b3e}.stage-event-copy>small{font:650 .75rem var(--awdp-mono);letter-spacing:.14em;color:#47a7cc}.stage-timeline{position:absolute;z-index:7;left:8%;right:8%;bottom:1rem;height:2px;background:rgba(100,177,207,.14)}.stage-timeline i{display:block;height:100%;background:linear-gradient(90deg,#028cff,#35e4ff);box-shadow:0 0 10px #00b4ff}.stage-idle{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;color:#16769b}.idle-orbit{position:absolute;left:50%;top:47%;translate:-50% -50%;border:1px solid rgba(0,193,255,.28);border-radius:50%;animation:idle-spin 14s linear infinite}.idle-orbit i{position:absolute;left:50%;top:-.22rem;width:.4rem;height:.4rem;border-radius:50%;background:#00c8ff;box-shadow:0 0 12px #00c8ff}.orbit-one{width:19rem;height:19rem}.orbit-two{width:28rem;height:28rem;border-style:dashed;animation-direction:reverse;animation-duration:22s}.idle-core{width:7rem;height:7rem;display:grid;place-items:center;border:1px solid rgba(0,217,255,.48);border-radius:50%;background:radial-gradient(circle,rgba(0,195,255,.15),transparent 65%);box-shadow:0 0 45px rgba(0,174,255,.12);animation:idle-pulse 2.4s ease-in-out infinite}.idle-core svg{width:2.7rem;height:2.7rem;stroke-width:1}.idle-mark{position:absolute;top:47%;width:2.1rem;height:2.1rem;opacity:.35}.idle-mark-left{left:13%}.idle-mark-right{right:13%}.stage-idle strong{margin-top:2rem;font:800 1rem var(--awdp-display);letter-spacing:.22em;color:#71bbd5}.stage-idle span{margin-top:.55rem;font:600 .68rem var(--awdp-mono);letter-spacing:.12em;color:#3f7790}.stage-scanline{position:absolute;inset-inline:0;top:-20%;height:24%;background:linear-gradient(transparent,rgba(0,200,255,.045),transparent);animation:scanline 8s linear infinite;pointer-events:none}.stage-footer{display:flex;align-items:center;justify-content:space-between;padding:0 1.15rem;border-top:1px solid rgba(0,150,213,.22);font:600 .64rem var(--awdp-mono);letter-spacing:.14em;color:#52778b}.stage-footer span:first-child{display:flex;align-items:center;gap:.45rem}.live-dot{width:.4rem;height:.4rem;border-radius:50%;background:#18f6a1;box-shadow:0 0 9px #18f6a1;animation:blink 1.4s ease-in-out infinite}.hud-corners i{position:absolute;width:1.1rem;height:1.1rem;border-color:#00bfff}.hud-corners i:nth-child(1){left:.5rem;top:.5rem;border-left:1px solid;border-top:1px solid}.hud-corners i:nth-child(2){right:.5rem;top:.5rem;border-right:1px solid;border-top:1px solid}.hud-corners i:nth-child(3){right:.5rem;bottom:.5rem;border-right:1px solid;border-bottom:1px solid}.hud-corners i:nth-child(4){left:.5rem;bottom:.5rem;border-left:1px solid;border-bottom:1px solid}.stage-swap-enter-active,.stage-swap-leave-active{transition:opacity .24s ease,filter .24s ease}.stage-swap-enter-from,.stage-swap-leave-to{opacity:0;filter:blur(6px)}@keyframes idle-spin{to{rotate:360deg}}@keyframes idle-pulse{50%{scale:1.08;box-shadow:0 0 65px rgba(0,197,255,.23)}}@keyframes scanline{to{top:110%}}@keyframes blink{50%{opacity:.25}}
</style>
