<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import HomeTeamListItem, { type HomeTeam } from '@/components/home/HomeTeamListItem.vue'

interface Props {
  teams: HomeTeam[]
  loading: boolean
}

defineProps<Props>()

const { t } = useI18n()
</script>

<template>
  <Card class="h-full min-h-[11rem]">
    <CardHeader class="flex flex-row items-start justify-between gap-4 border-b-2 border-border pb-3">
      <div>
        <h2 class="text-sm font-bold uppercase tracking-[0.14em]">{{ t('home.teamTitle') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ t('home.teamSubtitle') }}</p>
      </div>
      <Button variant="outline" size="sm" as-child>
        <RouterLink to="/teams">{{ t('nav.teams') }}</RouterLink>
      </Button>
    </CardHeader>

    <CardContent class="flex-1 py-4">
      <div v-if="loading" class="grid gap-3">
        <Skeleton v-for="i in 3" :key="i" class="h-20 border-2 border-border" />
      </div>

      <div
        v-else-if="teams.length === 0"
        class="flex min-h-40 flex-col items-center justify-center rounded-lg border-2 border-dashed border-border bg-muted p-8 text-center text-muted-foreground"
      >
        {{ t('teams.emptyMine') }}
      </div>

      <div v-else class="grid gap-3">
        <HomeTeamListItem
          v-for="team in teams"
          :key="team.id"
          :team="team"
        />
      </div>
    </CardContent>
  </Card>
</template>
