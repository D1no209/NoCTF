<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
</script>

<template>
  <Card class="transition-colors hover:border-primary/50">
    <CardHeader>
      <div class="flex items-start justify-between gap-2">
        <CardTitle class="text-base">
          <NuxtLink :to="`/competitions/${competition.id}`" class="hover:underline">
            {{ competition.title }}
          </NuxtLink>
        </CardTitle>
        <ModeBadge :mode="competition.mode" />
      </div>
      <CardDescription v-if="competition.description" class="line-clamp-2">
        {{ competition.description }}
      </CardDescription>
    </CardHeader>
    <CardContent class="flex flex-col gap-2 text-sm text-muted-foreground">
      <div class="flex items-center gap-2">
        <LifecycleBadge :status="competition.status" />
        <CompetitionCountdown
          :start-time="competition.startTime"
          :end-time="competition.endTime"
          :status="competition.status"
        />
      </div>
      <p>
        {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
      </p>
    </CardContent>
  </Card>
</template>
