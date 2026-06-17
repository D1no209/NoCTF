<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import EmptyState from '@/components/state/EmptyState.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { RouterLink } from 'vue-router'

const { t } = useI18n()
const search = ref('')
const statusFilter = ref('all')
const modeFilter = ref('all')

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
}

const { data: competitions, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<Competition[]>(),
})

const filteredCompetitions = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (competitions.value ?? []).filter((comp) => {
    const matchesSearch = !q || comp.title.toLowerCase().includes(q) || (comp.description ?? '').toLowerCase().includes(q)
    const matchesStatus = statusFilter.value === 'all' || comp.status.toLowerCase() === statusFilter.value
    const matchesMode = modeFilter.value === 'all' || (comp.gameModeType ?? '').toLowerCase() === modeFilter.value
    return matchesSearch && matchesStatus && matchesMode
  })
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
  <AppLayout>
    <div class="mx-auto w-full max-w-7xl space-y-6 px-4 py-6 md:px-6">
      <PageHeader :title="t('competitions.title')" :description="t('competitions.subtitle')">
        <template #actions>
          <Button variant="outline" size="sm" @click="refetch()">{{ t('common.refresh') }}</Button>
        </template>
      </PageHeader>

      <div class="grid gap-3 rounded-md border bg-card p-3 md:grid-cols-[1fr_180px_180px]">
        <Input v-model="search" :placeholder="t('competitions.searchPlaceholder')" />
        <Select v-model="statusFilter">
          <option value="all">{{ t('common.all') }} {{ t('common.status') }}</option>
          <option value="draft">{{ t('competitions.status.draft') }}</option>
          <option value="published">{{ t('competitions.status.published') }}</option>
          <option value="running">{{ t('competitions.status.running') }}</option>
          <option value="finished">{{ t('competitions.status.finished') }}</option>
        </Select>
        <Select v-model="modeFilter">
          <option value="all">{{ t('common.all') }} {{ t('common.mode') }}</option>
          <option value="ctf">CTF</option>
          <option value="awd">AWD</option>
          <option value="awdp">AWDP</option>
          <option value="koh">KoH</option>
        </Select>
      </div>

      <div v-if="isLoading" class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <Skeleton v-for="i in 6" :key="i" class="h-40" />
      </div>
      <ErrorState
        v-else-if="isError"
        :title="t('competitions.loadError')"
        :retry-label="t('common.refresh')"
        @retry="refetch()"
      />
      <EmptyState
        v-else-if="filteredCompetitions.length === 0"
        :title="t('competitions.empty')"
        :description="t('competitions.emptyDescription')"
        :action-label="t('common.refresh')"
        @action="refetch()"
      />

      <div v-else class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <RouterLink
          v-for="comp in filteredCompetitions"
          :key="comp.id"
          :to="`/competitions/${comp.id}`"
          class="block group"
        >
          <Card class="h-full cursor-pointer gap-4 transition-shadow group-hover:shadow-md">
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
            <CardContent class="space-y-3 text-xs text-muted-foreground">
              <div class="flex items-center justify-between">
                <Badge variant="outline">{{ comp.gameModeType ?? '-' }}</Badge>
                <span>{{ t('competitions.enter') }}</span>
              </div>
              <div class="space-y-1">
                <div>{{ t('competitions.startLabel') }} {{ formatDate(comp.startTime) }}</div>
                <div>{{ t('competitions.endLabel') }} {{ formatDate(comp.endTime) }}</div>
              </div>
            </CardContent>
          </Card>
        </RouterLink>
      </div>
    </div>
  </AppLayout>
</template>
