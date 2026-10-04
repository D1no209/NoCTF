<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpDefenseSuccessAnimationViewState } from '~/features/awdp-control/useAwdpDefenseSuccessAnimation'

const viewProps = defineProps<{ state: AwdpDefenseSuccessAnimationViewState }>()
const { Box, ScanLine, ShieldCheck, hexes, event } = toRefs(viewProps.state)
</script>

<template>
  <div class="event-fx defense-success" aria-hidden="true">
    <div class="upload-cube"><Box /><i /><span>{{ $t('competitions.label.patchUpload') }}</span></div>
    <div class="scan-disc"><ScanLine /><i /><strong>{{ $t('competitions.label.scan') }}</strong></div>
    <div class="shield-grid"><i v-for="hex in hexes" :key="hex ?? undefined" :style="{ '--hex': hex }" /></div>
    <div class="success-shield"><ShieldCheck /><i /></div>
    <div class="defense-result">
      <small>{{ event.teamName }} / {{ event.challengeTitle }}</small>
      <strong>{{ $t('competitions.label.defenseSuccess') }}</strong>
      <span>{{ $t('competitions.label.patchVerified') }} {{ $t('common.label.pendingRoundSettlement') }}</span>
    </div>
  </div>
</template>

<style scoped>
.event-fx{position:absolute;inset:0;overflow:hidden;color:var(--success)}.upload-cube{position:absolute;left:50%;top:58%;width:10rem;height:10rem;translate:-50% -50%;display:grid;place-items:center;animation:cube-upload 5.4s both}.upload-cube svg{width:5rem;height:5rem;stroke-width:1}.upload-cube i{position:absolute;bottom:-3rem;width:13rem;height:3rem;border:1px solid var(--primary);border-radius:50%;background:radial-gradient(ellipse,color-mix(in oklch, var(--success) 24%, transparent),transparent 65%);box-shadow:var(--panel-shadow)}.upload-cube span{position:absolute;bottom:-5rem;font:700 .78rem var(--awdp-mono);letter-spacing:.2em}.scan-disc{position:absolute;left:50%;top:48%;width:24rem;height:24rem;translate:-50% -50%;display:grid;place-items:center;border:1px dashed currentColor;border-radius:50%;opacity:0;animation:scan-stage 5.4s both}.scan-disc:before,.scan-disc:after{content:"";position:absolute;inset:2.5rem;border:1px solid color-mix(in oklch, var(--success) 42%, transparent);border-radius:50%}.scan-disc:after{inset:6rem}.scan-disc svg{width:6rem;height:6rem;stroke-width:.8}.scan-disc i{position:absolute;inset:0;border-radius:50%;background:conic-gradient(from 0deg,transparent 0 78%,color-mix(in oklch, var(--success) 30%, transparent) 96%,transparent);animation:spin 1.1s linear infinite}.scan-disc strong{position:absolute;bottom:3.5rem;font:900 1rem var(--awdp-display);letter-spacing:.3em}.shield-grid{position:absolute;left:50%;top:48%;width:22rem;height:25rem;translate:-50% -50%;display:grid;grid-template-columns:repeat(5,3.1rem);place-content:center;gap:.22rem;clip-path:polygon(50% 0,94% 18%,86% 76%,50% 100%,14% 76%,6% 18%);opacity:0;animation:grid-form 5.4s both}.shield-grid i{height:3.5rem;background:color-mix(in oklch, var(--success) 12%, transparent);border:1px solid color-mix(in oklch, var(--success) 50%, transparent);clip-path:polygon(25% 6%,75% 6%,100% 50%,75% 94%,25% 94%,0 50%);animation:hex-in .55s calc(2.05s + var(--hex)*24ms) both}.success-shield{position:absolute;left:50%;top:48%;width:17rem;height:20rem;translate:-50% -50%;display:grid;place-items:center;clip-path:polygon(50% 0,94% 18%,86% 76%,50% 100%,14% 76%,6% 18%);border:2px solid currentColor;background:linear-gradient(180deg,color-mix(in oklch, var(--success) 18%, transparent),color-mix(in oklch, var(--primary) 58%, transparent));opacity:0;animation:shield-close 5.4s both}.success-shield svg{width:8rem;height:8rem;stroke-width:1}.success-shield i{position:absolute;inset:-5rem;border:2px solid color-mix(in oklch, var(--success) 70%, transparent);border-radius:50%;animation:shield-pulse 1.2s 3.1s both}.defense-result{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;opacity:0;animation:result-in 5.4s both}.defense-result small{font:600 1rem var(--awdp-mono);letter-spacing:.18em;color:var(--success)}.defense-result strong{font:900 3.8rem var(--awdp-display);letter-spacing:.07em;text-shadow:none}.defense-result span{margin-top:.8rem;border:1px solid color-mix(in oklch, var(--success) 48%, transparent);padding:.42rem 1.2rem;font:700 .82rem var(--awdp-mono);letter-spacing:.16em}
.shield-grid i:nth-child(odd){--hex-shift:0rem}.shield-grid i:nth-child(even){--hex-shift:2rem}@keyframes cube-upload{0%{opacity:0;translate:-50% 7rem;rotate:-15deg}10%,18%{opacity:1;translate:-50% -50%;rotate:0}28%{opacity:0;translate:-50% -10rem;scale:.55}100%{opacity:0}}@keyframes scan-stage{0%,18%{opacity:0;scale:.7}27%,43%{opacity:1;scale:1}50%,100%{opacity:0;scale:1.2}}@keyframes spin{to{rotate:360deg}}@keyframes grid-form{0%,35%{opacity:0}44%,60%{opacity:1}70%,100%{opacity:0}}@keyframes hex-in{from{opacity:0;scale:.1;translate:var(--hex-shift) 3rem}to{opacity:1;scale:1}}@keyframes shield-close{0%,48%{opacity:0;scale:1.4}57%,69%{opacity:1;scale:1}76%,100%{opacity:0;scale:1.15}}@keyframes shield-pulse{0%{opacity:0;scale:.25}25%{opacity:1}100%{opacity:0;scale:1.6}}@keyframes result-in{0%,64%{opacity:0;translate:0 1.4rem}73%,90%{opacity:1;translate:0 0}100%{opacity:0;translate:0 -.5rem}}
</style>
