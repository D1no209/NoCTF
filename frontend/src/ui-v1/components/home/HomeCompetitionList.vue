<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader } from '@/ui-v1/components/ui/card'
import { Skeleton } from '@/ui-v1/components/ui/skeleton'
import { RouterLink } from 'vue-router'
import HomeCompetitionListItem, { type HomeCompetition } from '@/ui-v1/components/home/HomeCompetitionListItem.vue'

interface Props {
  competitions: HomeCompetition[]
  loading: boolean
}

defineProps<Props>()

const { t } = useI18n()
</script>

<template>
  <Card class="h-full min-h-[11rem]">
    <CardHeader class="flex flex-row items-start justify-between gap-4 border-b-2 border-border pb-3">
      <div>
        <h2 class="text-sm font-bold uppercase tracking-[0.14em]">{{ t('home.competitionTitle') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ t('home.competitionSubtitle', { count: competitions.length }) }}</p>
      </div>
      <Button variant="outline" size="sm" as-child>
        <RouterLink to="/competitions">{{ t('common.all') }}</RouterLink>
      </Button>
    </CardHeader>

    <CardContent class="flex-1 py-4">
      <div v-if="loading" class="grid gap-3">
        <Skeleton v-for="i in 3" :key="i" class="h-24 border-2 border-border" />
      </div>

      <div
        v-else-if="competitions.length === 0"
        class="flex min-h-40 flex-col items-center justify-center rounded-lg border-2 border-dashed border-border bg-muted p-8 text-center text-muted-foreground"
      >
        {{ t('competitions.empty') }}
      </div>

      <div v-else class="grid gap-3">
        <HomeCompetitionListItem
          v-for="competition in competitions"
          :key="competition.id"
          :competition="competition"
        />
      </div>
    </CardContent>
  </Card>
</template>
