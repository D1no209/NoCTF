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
import { 
  Select, 
  SelectContent, 
  SelectItem, 
  SelectTrigger, 
  SelectValue 
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import EmptyState from '@/components/state/EmptyState.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { RouterLink } from 'vue-router'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { Search, RotateCw, Calendar } from 'lucide-vue-next'

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
  const s = status.toLowerCase()
  if (s === 'active' || s === 'running') return 'default'
  if (s === 'upcoming' || s === 'pending') return 'secondary'
  if (s === 'ended' || s === 'finished') return 'outline'
  return 'secondary'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-7xl space-y-8 px-4 py-8 md:px-6">
      <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
        <PageHeader 
          :title="t('competitions.title')" 
          :description="t('competitions.subtitle')"
          class="flex-1"
        />
        <Button variant="outline" size="sm" @click="refetch()" :disabled="isLoading" class="shrink-0">
          <RotateCw class="mr-2 size-4" :class="{ 'animate-spin': isLoading }" />
          {{ t('common.refresh') }}
        </Button>
      </div>

      <!-- Filters -->
      <div class="grid gap-4 rounded-xl border bg-card/50 backdrop-blur-sm p-4 shadow-sm md:grid-cols-[1fr_200px_200px]">
        <div class="relative">
          <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="search" :placeholder="t('competitions.searchPlaceholder')" class="pl-10" />
        </div>
        
        <Select v-model="statusFilter">
          <SelectTrigger>
            <SelectValue :placeholder="t('common.status')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ t('common.all') }} {{ t('common.status') }}</SelectItem>
            <SelectItem value="draft">{{ t('competitions.status.draft') }}</SelectItem>
            <SelectItem value="published">{{ t('competitions.status.published') }}</SelectItem>
            <SelectItem value="running">{{ t('competitions.status.running') }}</SelectItem>
            <SelectItem value="finished">{{ t('competitions.status.finished') }}</SelectItem>
          </SelectContent>
        </Select>

        <Select v-model="modeFilter">
          <SelectTrigger>
            <SelectValue :placeholder="t('common.mode')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ t('common.all') }} {{ t('common.mode') }}</SelectItem>
            <SelectItem value="ctf">CTF</SelectItem>
            <SelectItem value="awd">AWD</SelectItem>
            <SelectItem value="awdp">AWDP</SelectItem>
            <SelectItem value="koh">KoH</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <!-- Content -->
      <div v-if="isLoading" class="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
        <Skeleton v-for="i in 6" :key="i" class="h-56 rounded-xl" />
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

      <div v-else v-auto-animate class="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
        <RouterLink
          v-for="comp in filteredCompetitions"
          :key="comp.id"
          :to="`/competitions/${comp.id}`"
          class="block group"
        >
          <Card class="h-full overflow-hidden border-border/50 transition-all duration-300 hover:shadow-xl hover:-translate-y-1 hover:border-primary/20">
            <CardHeader class="pb-3">
              <div class="flex items-start justify-between gap-2">
                <CardTitle class="text-lg font-bold group-hover:text-primary transition-colors">
                  {{ comp.title }}
                </CardTitle>
                <Badge :variant="statusVariant(comp.status)" class="shrink-0 font-bold uppercase tracking-tighter text-[10px]">
                  {{ comp.status }}
                </Badge>
              </div>
              <CardDescription v-if="comp.description" class="line-clamp-2 mt-2 leading-relaxed">
                {{ comp.description }}
              </CardDescription>
            </CardHeader>
            <CardContent class="space-y-4 pt-0">
              <div class="flex items-center gap-2">
                <Badge variant="secondary" class="bg-primary/5 text-primary border-primary/10">
                  {{ comp.gameModeType || 'CTF' }}
                </Badge>
              </div>
              
              <div class="space-y-2 text-xs text-muted-foreground border-t pt-4">
                <div class="flex items-center gap-2">
                  <Calendar class="size-3.5" />
                  <span>{{ formatDate(comp.startTime) }} — {{ formatDate(comp.endTime) }}</span>
                </div>
              </div>

              <Button variant="ghost" class="w-full mt-2 group-hover:bg-primary group-hover:text-primary-foreground transition-all">
                {{ t('competitions.enter') }}
              </Button>
            </CardContent>
          </Card>
        </RouterLink>
      </div>
    </div>
  </AppLayout>
</template>

<style scoped>
.group:hover .card {
  border-color: var(--primary);
}
</style>
