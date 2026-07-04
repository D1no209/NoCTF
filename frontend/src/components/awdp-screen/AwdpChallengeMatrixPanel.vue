<script setup lang="ts">
import type { AwdpChallengeCategory, AwdpChallengeStatus } from '@/types/awdpScreen'
import { Blocks, RotateCw } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  challenges: AwdpChallengeStatus[]
}>()

const PAGE_SIZE = 2
const ROTATE_MS = 7_200
const page = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const sortedChallenges = computed(() => [...props.challenges].sort((a, b) => b.attackHeat - a.attackHeat))
const pageCount = computed(() => Math.max(1, Math.ceil(sortedChallenges.value.length / PAGE_SIZE)))
const visibleChallenges = computed(() => sortedChallenges.value.slice(page.value * PAGE_SIZE, page.value * PAGE_SIZE + PAGE_SIZE))
const pageLabel = computed(() => `${page.value + 1}/${pageCount.value}`)

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

function categoryClass(category: AwdpChallengeCategory) {
  if (category === 'web')
    return 'category-web'
  if (category === 'pwn')
    return 'category-pwn'
  if (category === 'crypto')
    return 'category-crypto'
  if (category === 'reverse')
    return 'category-reverse'
  return 'category-misc'
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
  <section class="awdp-panel challenge-panel">
    <div class="challenge-head">
      <div>
        <h2>
          Challenge matrix
        </h2>
        <p>
          Heat, fix status, and activity, page {{ pageLabel }}
        </p>
      </div>
      <div class="challenge-head-icons">
        <RotateCw v-if="pageCount > 1" class="size-3.5" />
        <Blocks class="size-4" />
      </div>
    </div>

    <div v-if="sortedChallenges.length === 0" class="challenge-empty">
      No challenges are available for this screen.
    </div>

    <TransitionGroup
      v-else
      name="challenge-row"
      tag="div"
      class="challenge-list"
    >
      <article
        v-for="challenge in visibleChallenges"
        :key="challenge.challengeId"
        class="challenge-row"
      >
        <div class="challenge-main">
          <div class="challenge-title">
            <h3>{{ challenge.challengeName }}</h3>
            <span :class="categoryClass(challenge.category)">
              {{ challenge.category }}
            </span>
          </div>

          <div class="challenge-heat">
            <span>Heat</span>
            <strong>{{ challenge.attackHeat }}</strong>
            <div>
              <i :style="{ transform: `scaleX(${Math.min(100, challenge.attackHeat) / 100})` }" />
            </div>
          </div>
        </div>

        <div class="challenge-metrics">
          <div>
            <span>OK</span>
            <strong>{{ challenge.defensePassedCount }}</strong>
          </div>
          <div>
            <span>FAIL</span>
            <strong>{{ challenge.defenseFailedCount }}</strong>
          </div>
          <div>
            <span>INS</span>
            <strong>{{ challenge.instanceCount }}</strong>
          </div>
          <div>
            <span>ACT</span>
            <strong>{{ challenge.activeTeamCount }}</strong>
          </div>
        </div>

        <div class="challenge-time">
          last {{ formatTime(challenge.lastEventAt) }}
        </div>
      </article>
    </TransitionGroup>
  </section>
</template>

<style scoped>
.challenge-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.challenge-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  border-bottom: 1px solid var(--awdp-screen-border);
  padding: 0.62rem 0.82rem 0.55rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 58%, transparent);
}

.challenge-head h2 {
  margin: 0;
  color: var(--sidebar-foreground);
  font-size: 0.8rem;
  font-weight: 850;
  text-transform: uppercase;
}

.challenge-head p {
  margin: 0.12rem 0 0;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.66rem;
}

.challenge-head-icons {
  display: flex;
  align-items: center;
  gap: 0.38rem;
}

.challenge-empty {
  display: grid;
  flex: 1;
  place-items: center;
  color: color-mix(in oklch, var(--sidebar-foreground) 45%, transparent);
  font-size: 0.82rem;
  text-align: center;
}

.challenge-list {
  min-height: 0;
  flex: 1;
  padding: 0.5rem;
}

.challenge-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 0.7rem;
  min-height: calc((100% - 0.42rem) / 2);
  margin-bottom: 0.42rem;
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 10%, transparent);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 44%, var(--awdp-screen-bg));
  padding: 0.45rem 0.55rem;
}

.challenge-row:last-child {
  margin-bottom: 0;
}

.challenge-main {
  min-width: 0;
}

.challenge-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.challenge-title h3 {
  margin: 0;
  overflow: hidden;
  color: var(--sidebar-foreground);
  font-size: 0.76rem;
  font-weight: 820;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.challenge-title span {
  flex: 0 0 auto;
  border: 1px solid currentColor;
  border-radius: var(--radius-sm);
  padding: 0.12rem 0.34rem;
  font-size: 0.52rem;
  font-weight: 850;
  text-transform: uppercase;
}

.category-web {
  color: var(--awdp-break);
}

.category-pwn {
  color: var(--awdp-warn);
}

.category-crypto {
  color: var(--awdp-fix);
}

.category-reverse {
  color: color-mix(in oklch, var(--primary) 72%, var(--sidebar-foreground));
}

.category-misc {
  color: color-mix(in oklch, var(--sidebar-foreground) 62%, transparent);
}

.challenge-heat {
  display: grid;
  grid-template-columns: auto auto minmax(0, 1fr);
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.32rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.56rem;
  font-weight: 800;
  text-transform: uppercase;
}

.challenge-heat strong {
  color: color-mix(in oklch, var(--sidebar-foreground) 65%, transparent);
  font-variant-numeric: tabular-nums;
}

.challenge-heat div {
  height: 0.34rem;
  overflow: hidden;
  border-radius: var(--radius-lg);
  background: color-mix(in oklch, var(--sidebar-foreground) 8%, transparent);
}

.challenge-heat i {
  display: block;
  width: 100%;
  height: 100%;
  transform-origin: left center;
  border-radius: inherit;
  background: var(--awdp-break);
}

.challenge-metrics {
  display: grid;
  grid-template-columns: repeat(4, 2.35rem);
  gap: 0.28rem;
}

.challenge-metrics div {
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 9%, transparent);
  border-radius: var(--radius-sm);
  padding: 0.22rem 0.1rem;
  text-align: center;
}

.challenge-metrics span {
  display: block;
  color: color-mix(in oklch, var(--sidebar-foreground) 38%, transparent);
  font-size: 0.48rem;
  font-weight: 850;
}

.challenge-metrics strong {
  display: block;
  margin-top: 0.04rem;
  color: var(--sidebar-foreground);
  font-size: 0.72rem;
  font-weight: 850;
  font-variant-numeric: tabular-nums;
}

.challenge-time {
  width: 3.2rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 38%, transparent);
  font-size: 0.56rem;
  text-align: right;
}

.challenge-row-enter-active,
.challenge-row-leave-active {
  transition:
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.challenge-row-enter-from,
.challenge-row-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
