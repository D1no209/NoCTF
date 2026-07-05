<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface Challenge {
  id: string
  title: string
  points: number
  solveCount: number
  typeId?: string
  totalStageCount?: number | null
  solvedStageCount?: number | null
  totalScore?: number | null
}

const props = defineProps<{
  challenge: Challenge
  solved: boolean
}>()

const { t } = useI18n()

const pointsLabel = computed(() => `${props.challenge.points} ${t('nav.score')}`)
const isPenetration = computed(() => props.challenge.typeId?.toLowerCase() === 'penetration')
const stageLabel = computed(() => {
  const solved = props.challenge.solvedStageCount ?? 0
  const total = props.challenge.totalStageCount ?? 0
  return total > 0 ? `${solved}/${total}` : ''
})
</script>

<template>
  <Card
    class="relative h-full cursor-pointer overflow-hidden transition-[background-color,border-color,transform] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:-translate-y-0.5 hover:border-primary/30 hover:bg-accent/40"
    :class="solved ? 'border-success/40 bg-success-muted/50' : ''"
  >
    <div
      v-if="solved"
      class="pointer-events-none absolute right-3 top-3 rounded-md border px-2 py-1 text-[10px] font-bold uppercase leading-none tracking-[0.08em] noctf-status-success"
    >
      {{ t('challenges.attackSolved') }}
    </div>
    <CardHeader class="pb-2">
      <CardTitle class="text-sm font-semibold leading-snug" :class="solved ? 'pr-24' : ''">
        {{ challenge.title }}
      </CardTitle>
    </CardHeader>
    <CardContent class="flex items-center justify-between text-xs text-muted-foreground pt-0">
      <span class="font-medium text-foreground">{{ pointsLabel }}</span>
      <span v-if="isPenetration && stageLabel">{{ stageLabel }} {{ t('penetration.stages') }}</span>
      <span v-else>{{ t('challenges.solves', { count: challenge.solveCount }) }}</span>
    </CardContent>
  </Card>
</template>
