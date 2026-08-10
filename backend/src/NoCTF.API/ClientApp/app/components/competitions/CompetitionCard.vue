<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
</script>

<template>
  <NuxtLink
    :to="{ name: 'competitions-id', params: { id: competition.id } }"
    class="group block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
  >
    <Card class="h-full transition-all duration-200 group-hover:-translate-y-0.5 group-hover:border-primary/60">
      <CardHeader>
        <div class="flex items-center justify-between gap-2">
          <ModeBadge :mode="competition.mode" />
          <LifecycleBadge :status="competition.status" />
        </div>
        <CardTitle class="text-lg group-hover:text-primary">
          {{ competition.title }}
        </CardTitle>
        <CardDescription v-if="competition.description" class="line-clamp-2">
          {{ competition.description }}
        </CardDescription>
      </CardHeader>
      <CardContent class="mt-auto flex flex-col gap-1.5">
        <CompetitionCountdown
          :start-time="competition.startTime"
          :end-time="competition.endTime"
          :status="competition.status"
          class="font-mono text-lg font-semibold text-primary"
        />
        <p class="font-mono text-xs text-muted-foreground">
          {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
        </p>
      </CardContent>
    </Card>
  </NuxtLink>
</template>
