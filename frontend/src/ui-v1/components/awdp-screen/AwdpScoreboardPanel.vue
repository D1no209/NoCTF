<script setup lang="ts">
import type { AwdpTeamScore } from '@/types/awdpScreen'
import { RotateCw, Trophy } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Panel } from '@/ui-v1/components/ui/panel'
import AwdpScoreboardRow from './AwdpScoreboardRow.vue'

const props = defineProps<{
  teams: AwdpTeamScore[]
}>()

const { t } = useI18n()
const PAGE_SIZE = 8
const ROTATE_MS = 5_800
const page = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const pageCount = computed(() => Math.max(1, Math.ceil(props.teams.length / PAGE_SIZE)))
const visibleTeams = computed(() => props.teams.slice(page.value * PAGE_SIZE, page.value * PAGE_SIZE + PAGE_SIZE))
const pageLabel = computed(() => `${page.value + 1}/${pageCount.value}`)

const maxScores = computed(() => {
  const totals = props.teams.map(t => t.totalScore)
  const attacks = props.teams.map(t => t.attackScore)
  const defenses = props.teams.map(t => t.defenseScore)
  return {
    total: Math.max(1, ...totals),
    attack: Math.max(1, ...attacks),
    defense: Math.max(1, ...defenses),
  }
})

watch(pageCount, (count) => {
  page.value = Math.min(page.value, count - 1)
})

onMounted(() => {
  rotateTimer = setInterval(() => {
    if (pageCount.value > 1)
      page.value = (page.value + 1) % pageCount.value
  }, ROTATE_MS)
})

onUnmounted(() => {
  if (rotateTimer)
    clearInterval(rotateTimer)
})
</script>

<template>
  <Panel variant="default" class="flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-[var(--awdp-border)] px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-[var(--awdp-text)]">
          {{ t('awdpScreen.scoreboard.title') }}
        </h2>
        <p class="text-xs text-[var(--awdp-text-muted)]">
          {{ t('awdpScreen.scoreboard.subtitle') }}
        </p>
      </div>
      <div class="flex items-center gap-2 text-[11px] font-semibold uppercase text-[var(--awdp-text-muted)]">
        <RotateCw v-if="pageCount > 1" class="size-3.5 text-[var(--semantic-info)]" />
        <span>{{ pageLabel }}</span>
        <Trophy class="size-5 text-[var(--semantic-warning)]" />
      </div>
    </div>

    <div v-if="visibleTeams.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-[var(--awdp-text-muted)]">
      {{ t('awdpScreen.scoreboard.empty') }}
    </div>

    <TransitionGroup
      v-else
      name="score-row"
      tag="div"
      class="min-h-0 flex-1 space-y-1.5 px-2.5 py-2.5"
    >
      <AwdpScoreboardRow
        v-for="team in visibleTeams"
        :key="team.teamId"
        :team="team"
        :max-attack-score="maxScores.attack"
        :max-defense-score="maxScores.defense"
        :max-total-score="maxScores.total"
      />
    </TransitionGroup>
  </Panel>
</template>

<style scoped>
.score-row-move,
.score-row-enter-active,
.score-row-leave-active {
  transition:
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.score-row-enter-from,
.score-row-leave-to {
  opacity: 0;
  transform: translateY(8px);
}

@media (prefers-reduced-motion: reduce) {
  .score-row-move,
  .score-row-enter-active,
  .score-row-leave-active {
    transition: none;
  }
}
</style>
