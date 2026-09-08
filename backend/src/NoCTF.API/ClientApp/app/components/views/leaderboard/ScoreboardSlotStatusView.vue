<script setup lang="ts">
import { toRefs } from 'vue'
import type { ScoreboardSlotStatusViewState } from '~/features/leaderboard/useScoreboardSlotStatus'

const viewProps = defineProps<{ state: ScoreboardSlotStatusViewState }>()
const { Flag, Shield, ShieldCheck, ShieldX, signals, combinedSuccess, combinedFailure, noOperation, accessibleLabel } = toRefs(viewProps.state)
</script>

<template>
  <span class="inline-flex min-w-12 items-center justify-center gap-2" :title="accessibleLabel">
    <ShieldCheck v-if="combinedSuccess" class="size-5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
    <ShieldX v-else-if="combinedFailure" class="size-5 text-destructive" aria-hidden="true" />
    <span v-else-if="noOperation" class="font-mono text-sm text-muted-foreground/60" aria-hidden="true">-</span>
    <template v-else>
      <template v-if="signals.showFlag">
        <Flag v-if="signals.flagState === 'Succeeded'" class="size-4 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
        <Flag v-else-if="signals.flagState === 'Failed'" class="size-4 text-destructive" aria-hidden="true" />
      </template>
      <template v-if="signals.showShield">
        <Shield v-if="signals.shieldState === 'Succeeded'" class="size-4 text-primary" aria-hidden="true" />
        <ShieldX v-else-if="signals.shieldState === 'Failed'" class="size-4 text-destructive" aria-hidden="true" />
      </template>
    </template>
    <span class="sr-only">{{ accessibleLabel }}</span>
  </span>
</template>
