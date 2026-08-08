<script setup lang="ts">
import { Trophy } from '@lucide/vue'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
</script>

<template>
  <NuxtLink
    :to="{ name: 'competitions-id', params: { id: competition.id } }"
    class="group block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
  >
    <Card class="h-full overflow-hidden pt-0 transition-all duration-300 group-hover:-translate-y-1 group-hover:border-primary/50 group-hover:shadow-lg">
      <!-- 装饰横幅:品牌渐变 + 水印图标,状态徽章浮于右上 -->
      <div class="relative flex h-24 items-center justify-between bg-gradient-to-br from-primary/20 via-primary/8 to-transparent px-5">
        <Trophy class="absolute right-4 bottom-0 size-16 translate-y-1/4 text-primary/15" />
        <ModeBadge :mode="competition.mode" />
        <LifecycleBadge :status="competition.status" />
      </div>
      <CardHeader>
        <CardTitle class="text-base group-hover:text-primary group-hover:underline">
          {{ competition.title }}
        </CardTitle>
        <CardDescription v-if="competition.description" class="line-clamp-2">
          {{ competition.description }}
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-2 text-sm text-muted-foreground">
        <CompetitionCountdown
          :start-time="competition.startTime"
          :end-time="competition.endTime"
          :status="competition.status"
          class="font-medium text-primary"
        />
        <p class="font-mono text-xs tabular-nums">
          {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
        </p>
      </CardContent>
    </Card>
  </NuxtLink>
</template>
