<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpAttackSuccessAnimationViewState } from '~/features/awdp-control/useAwdpAttackSuccessAnimation'

const viewProps = defineProps<{ state: AwdpAttackSuccessAnimationViewState }>()
const { Crosshair, Swords, rays, shards, event } = toRefs(viewProps.state)
</script>

<template>
  <div class="event-fx attack-success" aria-hidden="true">
    <div class="attack-target">
      <i class="attack-ring ring-a" /><i class="attack-ring ring-b" /><i class="attack-ring ring-c" />
      <Crosshair class="target-icon" />
      <span class="target-label">{{ event.challengeTitle }}</span>
    </div>
    <div class="attack-rays">
      <i v-for="ray in rays" :key="ray" :style="{ '--ray': ray }" />
    </div>
    <div class="attack-core"><Swords /></div>
    <div class="attack-shards">
      <i v-for="shard in shards" :key="shard" :style="{ '--shard': shard }" />
    </div>
    <div class="attack-result">
      <small>{{ event.challengeTitle }}</small>
      <strong>{{ $t('ui.destroyed') }}</strong>
      <span>{{ $t('ui.attackVerified') }}</span>
    </div>
  </div>
</template>

<style scoped>
.event-fx{position:absolute;inset:0;overflow:hidden;color:var(--destructive);filter:drop-shadow(0 0 16px color-mix(in oklch, var(--destructive) 25%, transparent))}.attack-target{position:absolute;left:50%;top:48%;width:28rem;height:28rem;translate:-50% -50%;display:grid;place-items:center;animation:target-lock 5.4s both}.attack-ring{position:absolute;inset:0;border:2px solid currentColor;border-radius:50%;opacity:.68}.ring-a{animation:ring-in 1s cubic-bezier(.2,.8,.2,1) both,spin 4s linear infinite}.ring-b{inset:2.6rem;border-style:dashed;animation:ring-in .8s .12s both,spin-reverse 3s linear infinite}.ring-c{inset:6rem;border-width:1px;animation:ring-in .7s .2s both,pulse 1s ease-in-out infinite}.target-icon{width:6rem;height:6rem;stroke-width:1;animation:icon-lock 1s both}.target-label{position:absolute;bottom:4rem;font:700 1.1rem var(--awdp-mono);letter-spacing:.22em;text-transform:uppercase}.attack-rays{position:absolute;inset:0;animation:rays 5.4s both}.attack-rays i{--angle:calc(var(--ray) * 20deg);position:absolute;left:50%;top:48%;width:34rem;height:2px;transform-origin:left center;transform:rotate(var(--angle)) translateX(8rem) scaleX(0);background:linear-gradient(90deg,transparent,var(--foreground) 70%,var(--destructive));box-shadow:var(--panel-shadow);animation:ray-strike .9s calc(1.05s + var(--ray) * 14ms) cubic-bezier(.1,.7,.2,1) both}.attack-core{position:absolute;left:50%;top:48%;width:8rem;height:8rem;translate:-50% -50%;display:grid;place-items:center;border:1px solid currentColor;border-radius:50%;background:radial-gradient(circle,var(--foreground) 0 4%,var(--destructive) 12%,color-mix(in oklch, var(--destructive) 5%, transparent) 58%,transparent 70%);opacity:0;animation:core-burst 5.4s both}.attack-core svg{width:3rem}.attack-shards{position:absolute;left:50%;top:48%}.attack-shards i{position:absolute;width:1.6rem;height:.22rem;background:var(--destructive);clip-path:polygon(0 0,100% 50%,0 100%);transform:rotate(calc(var(--shard) * 18deg)) translateX(0);opacity:0;animation:shard-burst 1.2s calc(2.25s + var(--shard) * 9ms) ease-out both}.attack-result{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;opacity:0;animation:result-in 5.4s both}.attack-result small{font:600 1rem var(--awdp-mono);letter-spacing:.24em}.attack-result strong{font:900 4.7rem var(--awdp-display);letter-spacing:.06em;text-shadow:none}.attack-result span{margin-top:.65rem;border:1px solid color-mix(in oklch, var(--destructive) 60%, transparent);padding:.42rem 1.2rem;font:700 .85rem var(--awdp-mono);letter-spacing:.18em}
@keyframes target-lock{0%{scale:1.8;opacity:0}16%{scale:1;opacity:1}38%{scale:.86;opacity:1}52%{scale:1.12;opacity:0}100%{opacity:0}}@keyframes ring-in{from{scale:1.8;opacity:0}to{scale:1;opacity:.75}}@keyframes spin{to{rotate:360deg}}@keyframes spin-reverse{to{rotate:-360deg}}@keyframes pulse{50%{opacity:.22;scale:.96}}@keyframes icon-lock{0%{scale:.4;opacity:0}70%{scale:1.15;opacity:1}100%{scale:1}}@keyframes ray-strike{0%{transform:rotate(var(--angle)) translateX(28rem) scaleX(0);opacity:0}25%{opacity:1}75%{transform:rotate(var(--angle)) translateX(10rem) scaleX(1)}100%{transform:rotate(var(--angle)) translateX(2rem) scaleX(.1);opacity:0}}@keyframes core-burst{0%,35%{opacity:0;scale:.3}44%{opacity:1;scale:2.3}54%{opacity:.65;scale:1}70%,100%{opacity:0;scale:2.8}}@keyframes shard-burst{0%{opacity:0;transform:rotate(calc(var(--shard) * 18deg)) translateX(1rem)}20%{opacity:1}100%{opacity:0;transform:rotate(calc(var(--shard) * 18deg)) translateX(18rem)}}@keyframes rays{0%,100%{opacity:0}18%,48%{opacity:1}58%{opacity:0}}@keyframes result-in{0%,51%{opacity:0;scale:.75}61%,88%{opacity:1;scale:1}100%{opacity:0;scale:1.08}}
</style>
