<script setup lang="ts">
import { AlertTriangle, Check, Crosshair, Trophy, UsersRound } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandChallenge {
  id: string
  title: string
  description?: string | null
  typeId: string
  points: number
  solveCount: number
}

const props = defineProps<{
  challenges: CommandChallenge[]
  loading: boolean
  error?: boolean
  activeMode: string
}>()

defineEmits<{
  select: [challenge: CommandChallenge]
}>()

function challengeTone(typeId: string) {
  const type = typeId.toLowerCase()
  if (type === 'web' || type === 'pwn')
    return 'success'
  if (type === 'crypto' || type === 'reverse')
    return 'info'
  return 'warning'
}
</script>

<template>
  <CommandPanel class="challenge-grid">
    <header class="challenge-grid__header">
      <div>
        <CommandSignal :label="`${props.activeMode || 'CTF'} task feed`" tone="info" />
        <h2>Challenges</h2>
      </div>
      <span>{{ String(props.challenges.length).padStart(2, '0') }} loaded</span>
    </header>

    <div v-if="props.loading" class="challenge-grid__cards">
      <div v-for="item in 6" :key="item" class="challenge-grid__skeleton">
        <span />
        <span />
        <span />
      </div>
    </div>

    <div v-else-if="props.error" class="challenge-grid__empty">
      <AlertTriangle class="size-5 text-[var(--v2-danger)]" />
      <span>Challenge records could not be synchronized.</span>
    </div>

    <div v-else-if="props.challenges.length" class="challenge-grid__cards">
      <article v-for="challenge in props.challenges" :key="challenge.id" class="challenge-grid__card">
        <div class="challenge-grid__title">
          <CommandSignal :label="challenge.typeId" :tone="challengeTone(challenge.typeId)" />
          <h3>{{ challenge.title }}</h3>
        </div>
        <p>{{ challenge.description || 'No challenge brief published.' }}</p>
        <dl>
          <div>
            <dt><Trophy class="size-3.5" /> Score</dt>
            <dd>{{ challenge.points }}</dd>
          </div>
          <div>
            <dt><UsersRound class="size-3.5" /> Solves</dt>
            <dd>{{ challenge.solveCount }}</dd>
          </div>
        </dl>
        <CommandButton label="Inspect" tone="outline" @click="$emit('select', challenge)">
          <template #icon>
            <Crosshair class="size-4" />
          </template>
        </CommandButton>
      </article>
    </div>

    <div v-else class="challenge-grid__empty">
      <Check class="size-5 text-[var(--v2-text-muted)]" />
      <span>No challenge records are available for this competition.</span>
    </div>
  </CommandPanel>
</template>

<style scoped>
.challenge-grid { display: grid; min-height: 0; }
.challenge-grid__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 14px 16px; }
.challenge-grid__header h2 { margin: 5px 0 0; color: var(--v2-text); font-size: 16px; font-weight: 650; }
.challenge-grid__header > span { color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
.challenge-grid__cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
.challenge-grid__card { display: grid; min-width: 0; grid-template-rows: auto minmax(42px, 1fr) auto auto; gap: 12px; border-right: 1px solid rgb(26 58 103 / 0.7); border-bottom: 1px solid rgb(26 58 103 / 0.7); padding: 15px; }
.challenge-grid__card:nth-child(2n) { border-right: 0; }
.challenge-grid__title h3 { overflow: hidden; margin: 7px 0 0; color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; text-overflow: ellipsis; white-space: nowrap; }
.challenge-grid__card > p { display: -webkit-box; overflow: hidden; margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.55; -webkit-box-orient: vertical; -webkit-line-clamp: 2; }
.challenge-grid__card dl { display: flex; gap: 18px; margin: 0; }
.challenge-grid__card dl div { display: grid; gap: 3px; }
.challenge-grid__card dt { display: inline-flex; align-items: center; gap: 5px; color: var(--v2-text-faint); font-size: 9px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; }
.challenge-grid__card dd { margin: 0; color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; font-weight: 700; }
.challenge-grid__card :deep(.command-button) { width: 100%; }
.challenge-grid__skeleton { display: grid; min-height: 205px; grid-template-rows: 18px 1fr 34px; gap: 14px; border-right: 1px solid rgb(26 58 103 / 0.7); border-bottom: 1px solid rgb(26 58 103 / 0.7); padding: 15px; }
.challenge-grid__skeleton span { display: block; background: var(--v2-surface-hover); animation: command-pulse 1.2s ease-in-out infinite alternate; }
.challenge-grid__skeleton span:nth-child(1) { width: 45%; }
.challenge-grid__skeleton span:nth-child(3) { width: 75%; }
.challenge-grid__empty { display: flex; min-height: 150px; align-items: center; justify-content: center; gap: 9px; padding: 20px; color: var(--v2-text-muted); font-size: 12px; text-align: center; }

@keyframes command-pulse { to { opacity: 0.45; } }

@media (max-width: 640px) {
  .challenge-grid__cards { grid-template-columns: 1fr; }
  .challenge-grid__card, .challenge-grid__skeleton { border-right: 0; }
}
</style>
