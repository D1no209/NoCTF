<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpAttackFailureAnimationViewState } from '~/features/awdp-control/useAwdpAttackFailureAnimation'

const viewProps = defineProps<{ state: AwdpAttackFailureAnimationViewState }>()
const { Crosshair, ShieldX, bolts, event } = toRefs(viewProps.state)
</script>

<template>
  <div class="event-fx attack-failure" aria-hidden="true">
    <div class="failure-reticle"><i /><i /><Crosshair /></div>
    <div class="failure-bolts"><i v-for="bolt in bolts" :key="bolt" :style="{ '--bolt': bolt }" /></div>
    <div class="block-shield"><ShieldX /><strong>{{ $t('ui.blocked') }}</strong></div>
    <div class="failure-signal"><span v-for="index in 7" :key="index" /></div>
    <div class="failure-result">
      <small>{{ event.teamName }} / {{ event.challengeTitle }}</small>
      <strong>{{ $t('ui.attackFailed2') }}</strong>
      <span>{{ $t('ui.noPoints') }}</span>
    </div>
  </div>
</template>

<style scoped>
.event-fx{position:absolute;inset:0;overflow:hidden;color:#d72a35}.failure-reticle{position:absolute;left:50%;top:48%;width:25rem;height:25rem;translate:-50% -50%;display:grid;place-items:center;animation:reticle-fail 5.4s both}.failure-reticle i{position:absolute;inset:0;border:1px solid currentColor;border-radius:50%;box-shadow:inset 0 0 30px rgba(182,25,36,.12)}.failure-reticle i:nth-child(2){inset:4rem;border-style:dashed;animation:spin 3s linear infinite}.failure-reticle svg{width:5rem;height:5rem;stroke-width:1}.failure-bolts{position:absolute;inset:0}.failure-bolts i{--angle:calc(var(--bolt) * 30deg);position:absolute;left:50%;top:48%;width:28rem;height:2px;background:linear-gradient(90deg,#ff3030,transparent);transform-origin:left;transform:rotate(var(--angle)) translateX(24rem);animation:bolt-block .9s calc(.9s + var(--bolt)*25ms) both}.block-shield{position:absolute;left:50%;top:48%;width:15rem;height:17rem;translate:-50% -50%;display:grid;place-items:center;clip-path:polygon(50% 0,94% 18%,86% 76%,50% 100%,14% 76%,6% 18%);border:2px solid #ff3030;background:rgba(57,5,11,.72);opacity:0;animation:block-in 5.4s both}.block-shield svg{width:6rem;height:6rem}.block-shield strong{position:absolute;bottom:2.2rem;font:900 1.35rem var(--awdp-display);letter-spacing:.16em}.failure-signal{position:absolute;left:50%;top:48%;width:32rem;translate:-50% -50%;display:flex;gap:.45rem;opacity:0;animation:signal-die 5.4s both}.failure-signal span{height:3px;flex:1;background:#ff3030;animation:signal-flicker .16s steps(2) infinite}.failure-result{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;opacity:0;animation:result-in 5.4s both}.failure-result small{font:600 1rem var(--awdp-mono);letter-spacing:.18em;color:#ba6670}.failure-result strong{font:900 4rem var(--awdp-display);letter-spacing:.08em;color:#ff3030;text-shadow:0 0 26px rgba(182,25,36,.8)}.failure-result span{margin-top:.75rem;font:700 1rem var(--awdp-mono);letter-spacing:.2em;color:#9b5b61}
@keyframes spin{to{rotate:-360deg}}@keyframes reticle-fail{0%{opacity:0;scale:1.7}18%,43%{opacity:1;scale:1}52%{opacity:.25;scale:.9}62%,100%{opacity:0}}@keyframes bolt-block{0%{opacity:0;transform:rotate(var(--angle)) translateX(24rem) scaleX(.2)}35%{opacity:1}70%{transform:rotate(var(--angle)) translateX(8rem) scaleX(1)}100%{opacity:0;transform:rotate(var(--angle)) translateX(12rem) scaleX(-.35)}}@keyframes block-in{0%,31%{opacity:0;scale:.55}39%,55%{opacity:1;scale:1}60%{opacity:.2;scale:1.08}70%,100%{opacity:0}}@keyframes signal-die{0%,46%{opacity:0}51%,62%{opacity:1}70%,100%{opacity:0}}@keyframes signal-flicker{50%{opacity:.15}}@keyframes result-in{0%,57%{opacity:0;filter:blur(6px)}67%,88%{opacity:1;filter:blur(0)}100%{opacity:0;filter:blur(2px)}}
</style>
