<script setup lang="ts">
import { Clock3, RefreshCw, ShieldCheck, Swords } from 'lucide-vue-next'
import { computed } from 'vue'
import type { CommandAwdAttack, CommandAwdChallenge, CommandAwdService } from './awd-contract'
import CommandAwdAttackFeed from './CommandAwdAttackFeed.vue'
import CommandAwdFlagConsole from './CommandAwdFlagConsole.vue'
import CommandAwdServiceMatrix from './CommandAwdServiceMatrix.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  state: 'invalid' | 'loading' | 'error' | 'ready'
  errorMessage?: string
  round: number
  remainingSeconds: number
  totalSeconds: number
  services: CommandAwdService[]
  attacks: CommandAwdAttack[]
  challenges: CommandAwdChallenge[]
  selectedChallengeId: string
  flag: string
  submitPending: boolean
  operationMessage: string
  operationTone: 'success' | 'danger'
  signalConnected: boolean
}>()

const emit = defineEmits<{
  retry: []
  'update:selectedChallengeId': [value: string]
  'update:flag': [value: string]
  submit: []
}>()

const remainingText = computed(() => {
  const minutes = Math.floor(props.remainingSeconds / 60)
  const seconds = props.remainingSeconds % 60
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})
const progress = computed(() => props.totalSeconds > 0
  ? Math.max(0, Math.min(100, (props.remainingSeconds / props.totalSeconds) * 100))
  : 0)
const healthyCount = computed(() => props.services.filter(service => service.status === 'healthy').length)
const downCount = computed(() => props.services.filter(service => service.status === 'down').length)
</script>

<template>
  <section v-if="props.state === 'loading'" class="awd-dashboard__state">
    <CommandPanel class="awd-dashboard__skeleton" aria-busy="true"><span /><span /><span /></CommandPanel>
  </section>

  <section v-else-if="props.state === 'invalid' || props.state === 'error'" class="awd-dashboard__state">
    <CommandPanel class="awd-dashboard__error" tone="warning">
      <div>
        <CommandSignal :label="props.state === 'invalid' ? 'Invalid competition identifier' : 'Dashboard unavailable'" tone="danger" />
        <h1>Unable to load the AWD workspace</h1>
        <p>{{ props.errorMessage || 'The dashboard service did not return usable round data.' }}</p>
      </div>
      <CommandButton v-if="props.state === 'error'" label="Retry" @click="emit('retry')">
        <template #icon><RefreshCw class="size-4" /></template>
      </CommandButton>
    </CommandPanel>
  </section>

  <section v-else class="awd-dashboard">
    <header class="awd-dashboard__heading">
      <div>
        <CommandSignal label="AWD participant operations" tone="info" />
        <h1>Defense control room</h1>
      </div>
      <div class="awd-dashboard__connection">
        <CommandSignal :label="props.signalConnected ? 'Live SignalR' : 'SignalR reconnecting'" :tone="props.signalConnected ? 'success' : 'warning'" />
      </div>
    </header>

    <CommandPanel class="awd-dashboard__round">
      <div class="awd-dashboard__round-main">
        <Clock3 class="size-5 text-[var(--v2-warning)]" />
        <div>
          <span>Current round</span>
          <strong>R{{ String(props.round).padStart(2, '0') }}</strong>
        </div>
      </div>
      <div class="awd-dashboard__timer">
        <div><span>Time remaining</span><strong>{{ remainingText }}</strong></div>
        <i><em :style="{ width: `${progress}%` }" /></i>
      </div>
      <div class="awd-dashboard__round-stat">
        <ShieldCheck class="size-4 text-[var(--v2-cyan)]" />
        <span><b>{{ healthyCount }}</b> stable</span>
      </div>
      <div class="awd-dashboard__round-stat">
        <Swords class="size-4 text-[var(--v2-danger)]" />
        <span><b>{{ downCount }}</b> down</span>
      </div>
    </CommandPanel>

    <div class="awd-dashboard__grid">
      <CommandAwdServiceMatrix :services="props.services" />
      <div class="awd-dashboard__side">
        <CommandAwdFlagConsole
          :challenges="props.challenges"
          :selected-challenge-id="props.selectedChallengeId"
          :flag="props.flag"
          :pending="props.submitPending"
          :operation-message="props.operationMessage"
          :operation-tone="props.operationTone"
          @update:selected-challenge-id="emit('update:selectedChallengeId', $event)"
          @update:flag="emit('update:flag', $event)"
          @submit="emit('submit')"
        />
        <CommandAwdAttackFeed :attacks="props.attacks" />
      </div>
    </div>
  </section>
</template>

<style scoped>
.awd-dashboard,
.awd-dashboard__state { display: grid; gap: 16px; }
.awd-dashboard__heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; padding: 4px 2px 0; }
.awd-dashboard__heading h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 600; letter-spacing: -0.01em; }
.awd-dashboard__round { display: grid; grid-template-columns: minmax(150px, 0.7fr) minmax(230px, 1.4fr) minmax(100px, 0.45fr) minmax(90px, 0.4fr); align-items: center; gap: 8px; padding: 8px; }
.awd-dashboard__round-main,
.awd-dashboard__round-stat { display: flex; min-height: 76px; align-items: center; gap: 12px; border-radius: 12px; padding: 13px 16px; }
.awd-dashboard__round-main > div { display: grid; gap: 4px; }
.awd-dashboard__round-main span,
.awd-dashboard__timer span { color: var(--v2-text-muted); font-size: 10px; font-weight: 600; letter-spacing: 0.05em; }
.awd-dashboard__round-main strong { color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 24px; line-height: 1; }
.awd-dashboard__timer { min-width: 0; border-radius: 12px; padding: 12px 16px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.awd-dashboard__timer > div { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.awd-dashboard__timer strong { color: var(--v2-warning); font-family: var(--v2-font-mono); font-size: 20px; }
.awd-dashboard__timer i { display: block; height: 8px; margin-top: 10px; overflow: hidden; border-radius: 999px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); }
.awd-dashboard__timer em { display: block; height: 100%; border-radius: 999px; background: var(--v2-warning); }
.awd-dashboard__round-stat { justify-content: center; background: var(--v2-surface); box-shadow: var(--v2-inset); color: var(--v2-text-muted); font-size: 11px; }
.awd-dashboard__round-stat b { color: var(--v2-text); font-family: var(--v2-font-mono); }
.awd-dashboard__grid { display: grid; grid-template-columns: minmax(0, 1.35fr) minmax(300px, 0.65fr); align-items: start; gap: 16px; }
.awd-dashboard__side { display: grid; gap: 16px; }
.awd-dashboard__skeleton { display: grid; min-height: 320px; grid-template-rows: 32px 1fr 64px; gap: 16px; padding: 22px; }
.awd-dashboard__skeleton span { display: block; border-radius: 12px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); animation: awd-pulse 1.1s ease-in-out infinite alternate; }
.awd-dashboard__skeleton span:nth-child(1) { width: 30%; }
.awd-dashboard__skeleton span:nth-child(3) { width: 68%; }
.awd-dashboard__error { display: flex; min-height: 230px; align-items: center; justify-content: space-between; gap: 18px; padding: 24px; }
.awd-dashboard__error h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 19px; font-weight: 600; }
.awd-dashboard__error p { max-width: 650px; margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }

@keyframes awd-pulse { to { opacity: 0.45; } }

@media (max-width: 1080px) {
  .awd-dashboard__round { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .awd-dashboard__grid { grid-template-columns: 1fr; }
  .awd-dashboard__side { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 650px) {
  .awd-dashboard__heading,
  .awd-dashboard__error { align-items: flex-start; flex-direction: column; }
  .awd-dashboard__round { grid-template-columns: 1fr; }
  .awd-dashboard__side { grid-template-columns: 1fr; }
}
</style>
