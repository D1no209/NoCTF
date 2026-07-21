<script setup lang="ts">
import type { AwdpChallengeCategory, AwdpChallengeStatus } from '@/types/awdpScreen'
import { Blocks } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Panel } from '@/ui-v1/components/ui/panel'

const props = defineProps<{
  challenges: AwdpChallengeStatus[]
}>()

const { t } = useI18n()

const sortedChallenges = computed(() => [...props.challenges].sort((a, b) => b.attackHeat - a.attackHeat))

function categoryClass(category: AwdpChallengeCategory) {
  if (category === 'web')
    return 'text-[var(--awdp-category-web)] bg-[var(--semantic-info-soft)] border-[var(--semantic-info-border)]'
  if (category === 'pwn')
    return 'text-[var(--awdp-category-pwn)] bg-[var(--semantic-danger-soft)] border-[var(--semantic-danger-border)]'
  if (category === 'crypto')
    return 'text-[var(--awdp-category-crypto)] bg-[var(--semantic-neutral-soft)] border-[var(--awdp-border)]'
  if (category === 'reverse')
    return 'text-[var(--awdp-category-reverse)] bg-[var(--semantic-success-soft)] border-[var(--semantic-success-border)]'
  return 'text-[var(--awdp-category-misc)] bg-[var(--semantic-warning-soft)] border-[var(--semantic-warning-border)]'
}

function formatTime(value?: string) {
  if (!value)
    return t('awdpScreen.matrix.none')
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <Panel variant="dark" class="flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-[var(--awdp-border)] px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-[var(--awdp-text-inverse)]">
          {{ t('awdpScreen.matrix.title') }}
        </h2>
        <p class="text-xs text-[var(--awdp-text-muted)]">
          {{ t('awdpScreen.matrix.subtitle') }}
        </p>
      </div>
      <Blocks class="size-5 text-[var(--semantic-info)]" />
    </div>

    <div v-if="sortedChallenges.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-[var(--awdp-text-muted)]">
      {{ t('awdpScreen.matrix.empty') }}
    </div>

    <div v-else class="min-h-0 flex-1 overflow-y-auto p-3">
      <div class="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
        <article
          v-for="challenge in sortedChallenges"
          :key="challenge.challengeId"
          class="rounded-lg border border-[var(--awdp-border)] bg-[var(--awdp-surface-deep)] p-3"
        >
          <div class="mb-2 flex items-start justify-between gap-2">
            <div class="min-w-0">
              <h3 class="truncate text-sm font-semibold text-[var(--awdp-text-inverse)]">
                {{ challenge.challengeName }}
              </h3>
              <p class="mt-1 text-[11px] text-[var(--awdp-text-muted)]">
                {{ t('awdpScreen.matrix.lastPrefix', { time: formatTime(challenge.lastEventAt) }) }}
              </p>
            </div>
            <span class="shrink-0 rounded-md border px-2 py-1 text-[10px] font-bold uppercase" :class="categoryClass(challenge.category)">
              {{ challenge.category }}
            </span>
          </div>

          <div class="mb-3">
            <div class="mb-1 flex justify-between text-[11px] text-[var(--awdp-text-muted)]">
              <span>{{ t('awdpScreen.matrix.attackHeat') }}</span>
              <span class="font-mono">{{ challenge.attackHeat }}</span>
            </div>
            <div class="h-1.5 overflow-hidden rounded-full bg-[var(--awdp-surface-muted)]">
              <div
                class="h-full rounded-full bg-[var(--semantic-info)] transition-[width] duration-300"
                :style="{ width: `${Math.min(100, challenge.attackHeat)}%` }"
              />
            </div>
          </div>

          <div class="grid grid-cols-4 gap-2 text-center">
            <div class="rounded-[0.45rem] border border-[var(--awdp-border)] bg-[var(--awdp-surface-muted)] px-1 py-[0.35rem]">
              <span class="block text-[0.62rem] font-bold text-[var(--awdp-text-muted)]">{{ t('awdpScreen.matrix.labels.dp') }}</span>
              <strong class="mt-[0.15rem] block font-mono text-[0.82rem] text-[var(--awdp-text-inverse)]">{{ challenge.defensePassedCount }}</strong>
            </div>
            <div class="rounded-[0.45rem] border border-[var(--awdp-border)] bg-[var(--awdp-surface-muted)] px-1 py-[0.35rem]">
              <span class="block text-[0.62rem] font-bold text-[var(--awdp-text-muted)]">{{ t('awdpScreen.matrix.labels.df') }}</span>
              <strong class="mt-[0.15rem] block font-mono text-[0.82rem] text-[var(--awdp-text-inverse)]">{{ challenge.defenseFailedCount }}</strong>
            </div>
            <div class="rounded-[0.45rem] border border-[var(--awdp-border)] bg-[var(--awdp-surface-muted)] px-1 py-[0.35rem]">
              <span class="block text-[0.62rem] font-bold text-[var(--awdp-text-muted)]">{{ t('awdpScreen.matrix.labels.ins') }}</span>
              <strong class="mt-[0.15rem] block font-mono text-[0.82rem] text-[var(--awdp-text-inverse)]">{{ challenge.instanceCount }}</strong>
            </div>
            <div class="rounded-[0.45rem] border border-[var(--awdp-border)] bg-[var(--awdp-surface-muted)] px-1 py-[0.35rem]">
              <span class="block text-[0.62rem] font-bold text-[var(--awdp-text-muted)]">{{ t('awdpScreen.matrix.labels.act') }}</span>
              <strong class="mt-[0.15rem] block font-mono text-[0.82rem] text-[var(--awdp-text-inverse)]">{{ challenge.activeTeamCount }}</strong>
            </div>
          </div>
        </article>
      </div>
    </div>
  </Panel>
</template>
