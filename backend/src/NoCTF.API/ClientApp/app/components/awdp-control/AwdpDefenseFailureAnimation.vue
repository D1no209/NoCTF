<script setup lang="ts">
import { Box, ScanLine, ShieldAlert } from '@lucide/vue'
import type { AwdpControlEvent } from '~/utils/awdp-control-screen'

defineProps<{ event: AwdpControlEvent }>()
const cracks = Array.from({ length: 10 }, (_, index) => index)
</script>

<template>
  <div class="event-fx defense-failure" aria-hidden="true">
    <div class="upload-cube"><Box /><span>PATCH.UPLOAD</span></div>
    <div class="scan-warning"><ScanLine /><i /><strong>SCAN</strong></div>
    <div class="warning-mark"><ShieldAlert /><b>!</b><span>VALIDATION ERROR</span></div>
    <div class="cracked-shield"><i v-for="crack in cracks" :key="crack" :style="{ '--crack': crack }" /></div>
    <div class="defense-result">
      <small>{{ event.teamName }} / {{ event.challengeTitle }}</small>
      <strong>DEFENSE FAILED</strong>
      <span>PATCH REJECTED · {{ $t('无积分') }}</span>
    </div>
  </div>
</template>

<style scoped>
.event-fx{position:absolute;inset:0;overflow:hidden;color:#ff8500}.upload-cube{position:absolute;left:50%;top:58%;width:10rem;height:10rem;translate:-50% -50%;display:grid;place-items:center;animation:cube-upload 5.4s both}.upload-cube svg{width:5rem;height:5rem;stroke-width:1}.upload-cube:after{content:"";position:absolute;bottom:-3rem;width:13rem;height:3rem;border:1px solid #ff8500;border-radius:50%;background:radial-gradient(ellipse,rgba(255,106,0,.22),transparent 65%)}.upload-cube span{position:absolute;bottom:-5rem;font:700 .78rem var(--awdp-mono);letter-spacing:.2em}.scan-warning{position:absolute;left:50%;top:48%;width:23rem;height:23rem;translate:-50% -50%;display:grid;place-items:center;border:1px dashed currentColor;border-radius:50%;opacity:0;animation:scan-stage 5.4s both}.scan-warning svg{width:6rem;height:6rem;stroke-width:.8}.scan-warning i{position:absolute;inset:0;border-radius:50%;background:conic-gradient(transparent 0 75%,rgba(255,138,0,.35));animation:spin 1s linear infinite}.scan-warning strong{position:absolute;bottom:3rem;font:900 1rem var(--awdp-display);letter-spacing:.3em}.warning-mark{position:absolute;left:50%;top:48%;width:15rem;height:17rem;translate:-50% -50%;display:grid;place-items:center;opacity:0;animation:warning-in 5.4s both}.warning-mark svg{position:absolute;width:13rem;height:13rem;stroke-width:.75}.warning-mark b{font:900 5rem var(--awdp-display);animation:warning-blink .28s steps(2) infinite}.warning-mark span{position:absolute;bottom:0;font:800 .82rem var(--awdp-mono);letter-spacing:.18em}.cracked-shield{position:absolute;left:50%;top:48%;width:18rem;height:21rem;translate:-50% -50%;clip-path:polygon(50% 0,94% 18%,86% 76%,50% 100%,14% 76%,6% 18%);border:2px solid currentColor;background:rgba(73,27,0,.26);opacity:0;animation:shield-break 5.4s both}.cracked-shield i{--angle:calc(-70deg + var(--crack)*15deg);position:absolute;left:50%;top:45%;width:9rem;height:2px;transform-origin:left;transform:rotate(var(--angle));background:linear-gradient(90deg,#fff,#ff6a00,transparent);animation:crack-grow .65s calc(2.65s + var(--crack)*22ms) both}.defense-result{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;opacity:0;animation:result-in 5.4s both}.defense-result small{font:600 1rem var(--awdp-mono);letter-spacing:.18em;color:#d89b65}.defense-result strong{font:900 3.8rem var(--awdp-display);letter-spacing:.07em;color:#ff8500;text-shadow:0 0 30px rgba(255,106,0,.6)}.defense-result span{margin-top:.8rem;border:1px solid rgba(255,138,0,.48);padding:.42rem 1.2rem;font:700 .82rem var(--awdp-mono);letter-spacing:.16em}
@keyframes cube-upload{0%{opacity:0;translate:-50% 7rem;rotate:15deg}10%,18%{opacity:1;translate:-50% -50%;rotate:0}28%{opacity:0;translate:-50% -10rem;scale:.55}100%{opacity:0}}@keyframes scan-stage{0%,18%{opacity:0;scale:.7}27%,40%{opacity:1;scale:1}49%,100%{opacity:0;scale:1.2}}@keyframes spin{to{rotate:360deg}}@keyframes warning-in{0%,37%{opacity:0;scale:.5}46%,59%{opacity:1;scale:1}67%,100%{opacity:0;scale:1.08}}@keyframes warning-blink{50%{opacity:.25}}@keyframes shield-break{0%,47%{opacity:0;scale:1.25}56%,70%{opacity:1;scale:1}78%,100%{opacity:0;scale:.9}}@keyframes crack-grow{from{scale:0;opacity:0}to{scale:1;opacity:1}}@keyframes result-in{0%,64%{opacity:0;filter:blur(5px)}73%,90%{opacity:1;filter:blur(0)}100%{opacity:0;filter:blur(3px)}}
</style>
