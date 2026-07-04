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
    class="relative h-full cursor-pointer overflow-hidden transition-all hover:border-primary/30 hover:shadow-[0_12px_34px_rgb(15_23_42/0.08)]"
    :class="solved ? 'border-green-500/50 bg-green-500/5' : ''"
  >
    <div
      v-if="solved"
      class="pointer-events-none absolute right-3 top-3 rounded-md border border-emerald-600/45 bg-emerald-50 px-2 py-1 text-[10px] font-bold uppercase leading-none tracking-[0.08em] text-emerald-700"
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
