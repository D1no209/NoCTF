<script setup lang="ts">
import type { AwdpChallengeCategory, AwdpChallengeStatus } from '@/types/awdpScreen'
import { Blocks } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{
  challenges: AwdpChallengeStatus[]
}>()

const sortedChallenges = computed(() => [...props.challenges].sort((a, b) => b.attackHeat - a.attackHeat))

function categoryClass(category: AwdpChallengeCategory) {
  if (category === 'web')
    return 'text-cyan-100 bg-cyan-300/10 border-cyan-300/20'
  if (category === 'pwn')
    return 'text-orange-100 bg-orange-300/10 border-orange-300/20'
  if (category === 'crypto')
    return 'text-emerald-100 bg-emerald-300/10 border-emerald-300/20'
  if (category === 'reverse')
    return 'text-violet-100 bg-violet-300/10 border-violet-300/20'
  return 'text-slate-100 bg-slate-300/10 border-slate-300/20'
}

function formatTime(value?: string) {
  if (!value)
    return 'none'
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <section class="awdp-panel flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-slate-200/10 px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-slate-100">
          Challenge matrix
        </h2>
        <p class="text-xs text-slate-500">
          Heat, defense status, and activity
        </p>
      </div>
      <Blocks class="size-5 text-cyan-100" />
    </div>

    <div v-if="sortedChallenges.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      No challenges are available for this screen.
    </div>

    <div v-else class="noctf-scrollbar min-h-0 flex-1 overflow-y-auto p-3">
      <div class="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
        <article
          v-for="challenge in sortedChallenges"
          :key="challenge.challengeId"
          class="rounded-lg border border-slate-300/10 bg-slate-950/46 p-3"
        >
          <div class="mb-2 flex items-start justify-between gap-2">
            <div class="min-w-0">
              <h3 class="truncate text-sm font-semibold text-slate-100">
                {{ challenge.challengeName }}
              </h3>
              <p class="mt-1 text-[11px] text-slate-500">
                last {{ formatTime(challenge.lastEventAt) }}
              </p>
            </div>
            <span class="shrink-0 rounded-md border px-2 py-1 text-[10px] font-bold uppercase" :class="categoryClass(challenge.category)">
              {{ challenge.category }}
            </span>
          </div>

          <div class="mb-3">
            <div class="mb-1 flex justify-between text-[11px] text-slate-500">
              <span>Attack heat</span>
              <span class="font-mono">{{ challenge.attackHeat }}</span>
            </div>
            <div class="h-1.5 overflow-hidden rounded-full bg-slate-800">
              <div
                class="h-full rounded-full bg-cyan-200 transition-[width] duration-300"
                :style="{ width: `${Math.min(100, challenge.attackHeat)}%` }"
              />
            </div>
          </div>

          <div class="grid grid-cols-4 gap-2 text-center">
            <div class="awdp-mini-cell">
              <span>DP</span>
              <strong>{{ challenge.defensePassedCount }}</strong>
            </div>
            <div class="awdp-mini-cell">
              <span>DF</span>
              <strong>{{ challenge.defenseFailedCount }}</strong>
            </div>
            <div class="awdp-mini-cell">
              <span>INS</span>
              <strong>{{ challenge.instanceCount }}</strong>
            </div>
            <div class="awdp-mini-cell">
              <span>ACT</span>
              <strong>{{ challenge.activeTeamCount }}</strong>
            </div>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.awdp-mini-cell {
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 12%, transparent);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--sidebar) 82%, black);
  padding: 0.35rem 0.25rem;
}

.awdp-mini-cell span {
  display: block;
  color: color-mix(in oklch, var(--sidebar-foreground) 45%, transparent);
  font-size: 0.62rem;
  font-weight: 700;
}

.awdp-mini-cell strong {
  display: block;
  margin-top: 0.15rem;
  color: var(--sidebar-foreground);
  font-size: 0.82rem;
  font-variant-numeric: tabular-nums;
}
</style>
