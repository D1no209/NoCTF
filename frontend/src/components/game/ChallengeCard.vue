<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'

interface Challenge {
  id: string
  title: string
  typeId: string
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
    class="h-full transition-all hover:shadow-md cursor-pointer"
    :class="solved ? 'border-green-500/50 bg-green-500/5' : ''"
  >
    <CardHeader class="pb-2">
      <div class="flex items-start justify-between gap-2">
        <CardTitle class="text-sm font-semibold leading-snug">{{ challenge.title }}</CardTitle>
        <Badge v-if="solved" class="shrink-0 bg-green-600 text-white border-transparent">
          {{ t('challenges.solved') }}
        </Badge>
      </div>
      <div class="text-xs text-muted-foreground mt-1">{{ challenge.typeId }}</div>
    </CardHeader>
    <CardContent class="flex items-center justify-between text-xs text-muted-foreground pt-0">
      <span class="font-medium text-foreground">{{ pointsLabel }}</span>
      <span>{{ t('challenges.solves', { count: challenge.solveCount }) }}</span>
    </CardContent>
  </Card>
</template>
