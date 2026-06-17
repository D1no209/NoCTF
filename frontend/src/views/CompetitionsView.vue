<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import { client } from '@/api/generated/client.gen'
import { useAuthStore } from '@/stores/auth'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { RouterLink, useRouter } from 'vue-router'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  startTime: string
  endTime: string
}

const { data: competitions, isLoading, isError } = useQuery({
  queryKey: ['competitions'],
  queryFn: async () => {
    const res = await client.get<{ 200: Competition[] }, unknown, false>({
      url: '/api/competitions',
    })
    return res.data ?? []
  },
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 'Active' || status === 'Running') return 'default'
  if (status === 'Upcoming' || status === 'Pending') return 'secondary'
  if (status === 'Ended' || status === 'Finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}
</script>

<template>
  <div class="min-h-screen bg-background p-8">
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between mb-8">
        <h1 class="text-3xl font-bold tracking-tight">{{ t('competitions.title') }}</h1>
        <Button variant="outline" @click="async () => { auth.logout(); await router.push('/login') }">{{ t('auth.logout') }}</Button>
      </div>

      <div v-if="isLoading" class="text-muted-foreground">{{ t('competitions.loading') }}</div>
      <div v-else-if="isError" class="text-destructive">{{ t('competitions.loadError') }}</div>
      <div v-else-if="!competitions || competitions.length === 0" class="text-muted-foreground">
        {{ t('competitions.empty') }}
      </div>

      <div v-else class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        <RouterLink
          v-for="comp in competitions"
          :key="comp.id"
          :to="`/competitions/${comp.id}`"
          class="block group"
        >
          <Card class="h-full transition-shadow group-hover:shadow-md cursor-pointer">
            <CardHeader class="pb-2">
              <div class="flex items-start justify-between gap-2">
                <CardTitle class="text-base leading-snug">{{ comp.title }}</CardTitle>
                <Badge :variant="statusVariant(comp.status)" class="shrink-0">
                  {{ comp.status }}
                </Badge>
              </div>
              <CardDescription v-if="comp.description" class="line-clamp-2 mt-1">
                {{ comp.description }}
              </CardDescription>
            </CardHeader>
            <CardContent class="text-xs text-muted-foreground space-y-1">
              <div>{{ t('competitions.startLabel') }} {{ formatDate(comp.startTime) }}</div>
              <div>{{ t('competitions.endLabel') }} {{ formatDate(comp.endTime) }}</div>
            </CardContent>
          </Card>
        </RouterLink>
      </div>
    </div>
  </div>
</template>
