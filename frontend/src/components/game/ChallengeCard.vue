<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface Challenge {
  id: string
  title: string
  points: number
  solveCount: number
}

const props = defineProps<{
  challenge: Challenge
  solved: boolean
}>()

const { t } = useI18n()

const pointsLabel = computed(() => `${props.challenge.points} ${t('nav.score')}`)
</script>

<template>
  <Card
    class="relative h-full cursor-pointer overflow-hidden transition-all hover:border-primary/30 hover:shadow-[0_16px_45px_rgb(79_70_229/0.12)]"
    :class="solved ? 'border-green-500/50 bg-green-500/5' : ''"
  >
    <div
      v-if="solved"
      class="pointer-events-none absolute right-3 top-3 rounded-md border border-emerald-600/45 bg-emerald-50 px-2 py-1 text-[10px] font-bold uppercase leading-none tracking-[0.08em] text-emerald-700"
    >
      {{ t('challenges.attackSolved') }}
    </div>
    <CardHeader class="pb-2">
      <CardTitle class="text-sm font-semibold leading-snug" :class="solved ? 'pr-24' : ''">{{ challenge.title }}</CardTitle>
    </CardHeader>
    <CardContent class="flex items-center justify-between text-xs text-muted-foreground pt-0">
      <span class="font-medium text-foreground">{{ pointsLabel }}</span>
      <span>{{ t('challenges.solves', { count: challenge.solveCount }) }}</span>
    </CardContent>
  </Card>
</template>
