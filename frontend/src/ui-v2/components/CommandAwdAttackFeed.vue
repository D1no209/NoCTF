<script setup lang="ts">
import { Radio } from 'lucide-vue-next'
import { computed } from 'vue'
import type { CommandAwdAttack } from './awd-contract'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  attacks: CommandAwdAttack[]
}>()

const visibleAttacks = computed(() => props.attacks.slice(0, 12))

function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return '--:--:--'
  return new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(date)
}
</script>

<template>
  <CommandPanel class="awd-attack-feed">
    <header class="awd-attack-feed__header">
      <div>
        <CommandSignal label="SignalR stream" tone="success" />
        <h2>Attack feed</h2>
      </div>
      <Radio class="size-4 text-[var(--v2-cyan)]" />
    </header>
    <div v-if="visibleAttacks.length === 0" class="awd-attack-feed__empty">
      No live attacks received.
    </div>
    <div v-else class="awd-attack-feed__rows">
      <article v-for="attack in visibleAttacks" :key="attack.id" class="awd-attack-feed__row">
        <span>{{ formatTime(attack.timestamp) }}</span>
        <i aria-hidden="true" />
        <div>
          <strong>{{ attack.attackerTeamName }} <em>-&gt;</em> {{ attack.victimTeamName }}</strong>
          <small>R{{ attack.roundNumber }} / {{ attack.challengeName }}</small>
        </div>
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awd-attack-feed { display: flex; min-height: 0; flex-direction: column; }
.awd-attack-feed__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 16px 18px 8px; }
.awd-attack-feed__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.awd-attack-feed__rows { padding: 0 18px 10px; }
.awd-attack-feed__row { display: grid; min-height: 54px; grid-template-columns: 63px 4px minmax(0, 1fr); align-items: center; gap: 10px; box-shadow: inset 0 -2px 0 rgb(184 188 194 / 0.5), inset 0 -1px 0 rgb(255 255 255 / 0.85); }
.awd-attack-feed__row:last-child { box-shadow: none; }
.awd-attack-feed__row > span { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.awd-attack-feed__row > i { height: 26px; border-radius: 999px; background: var(--v2-primary); }
.awd-attack-feed__row div { min-width: 0; }
.awd-attack-feed__row strong,
.awd-attack-feed__row small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.awd-attack-feed__row strong { color: var(--v2-text); font-size: 12px; font-weight: 600; }
.awd-attack-feed__row strong em { color: var(--v2-danger); font-style: normal; }
.awd-attack-feed__row small { margin-top: 4px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 10px; }
.awd-attack-feed__empty { display: grid; min-height: 190px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 13px; }
</style>
