<script setup lang="ts">
import { onMounted, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
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
import { RouterLink } from 'vue-router'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { AlertCircle, ArrowRight, Calendar, Inbox, RotateCw, Search } from 'lucide-vue-next'

const { t } = useI18n()

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
}

type LoadState = 'loading' | 'error' | 'empty' | 'filtered-empty' | 'ready'

const state = reactive({
  search: '',
  statusFilter: 'all',
  modeFilter: 'all',
  competitions: [] as Competition[],
  visibleCompetitions: [] as Competition[],
  loadState: 'loading' as LoadState,
})

function resetFilters() {
  state.search = ''
  state.statusFilter = 'all'
  state.modeFilter = 'all'
}

function isState(loadState: LoadState) {
  return state.loadState === loadState
}

async function loadCompetitions() {
  state.loadState = 'loading'

  try {
    const response = await competitionApi.list<Competition[]>()
    state.competitions = Array.isArray(response) ? response : []
    updateListState()
  } catch {
    state.competitions = []
    state.visibleCompetitions = []
    state.loadState = 'error'
  }
}

onMounted(() => {
  void loadCompetitions()
})

function updateListState() {
  const q = state.search.trim().toLowerCase()
  state.visibleCompetitions = state.competitions.filter((comp) => {
    const matchesSearch = !q || comp.title.toLowerCase().includes(q) || (comp.description ?? '').toLowerCase().includes(q)
    const matchesStatus = state.statusFilter === 'all' || comp.status.toLowerCase() === state.statusFilter
    const matchesMode = state.modeFilter === 'all' || (comp.gameModeType ?? '').toLowerCase() === state.modeFilter
    return matchesSearch && matchesStatus && matchesMode
  })

  if (state.competitions.length === 0) {
    state.loadState = 'empty'
  } else if (state.visibleCompetitions.length === 0) {
    state.loadState = 'filtered-empty'
  } else {
    state.loadState = 'ready'
  }
}

watch(() => [state.search, state.statusFilter, state.modeFilter], () => {
  if (state.loadState !== 'loading' && state.loadState !== 'error') {
    updateListState()
  }
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
    <div class="mx-auto w-full max-w-[1600px] space-y-8 px-4 py-8 md:px-6">
      <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
        <PageHeader 
          :title="t('competitions.title')" 
          :description="t('competitions.subtitle')"
          class="flex-1"
        />
        <Button variant="outline" size="sm" @click="loadCompetitions()" :disabled="isState('loading')" class="shrink-0">
          <RotateCw class="mr-2 size-4" :class="{ 'animate-spin': isState('loading') }" />
          {{ t('common.refresh') }}
        </Button>
      </div>

      <!-- Filters -->
      <div class="noctf-panel grid gap-4 rounded-xl p-5 md:grid-cols-[1fr_220px_220px_auto]">
        <div class="relative">
          <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="state.search" :placeholder="t('competitions.searchPlaceholder')" class="pl-10" />
        </div>
        
        <Select v-model="state.statusFilter">
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

        <Select v-model="state.modeFilter">
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

        <Button variant="outline" @click="loadCompetitions()" :disabled="isState('loading')" class="hidden md:inline-flex">
          <RotateCw class="size-4" :class="{ 'animate-spin': isState('loading') }" />
          {{ t('common.refresh') }}
        </Button>
      </div>

      <div class="min-h-40">
        <div v-if="isState('loading')" class="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-3">
          <Skeleton v-for="i in 6" :key="i" class="h-64 rounded-xl" />
        </div>

        <div v-else-if="isState('error')" class="flex min-h-40 flex-col items-center justify-center rounded-md border border-dashed bg-muted/20 px-4 py-8 text-center">
          <AlertCircle class="size-8 text-destructive" />
          <h3 class="mt-3 text-sm font-medium">{{ t('competitions.loadError') }}</h3>
          <Button variant="outline" size="sm" class="mt-4" @click="loadCompetitions">
            {{ t('common.refresh') }}
          </Button>
        </div>

        <div v-else-if="isState('empty')" class="flex min-h-40 flex-col items-center justify-center rounded-md border border-dashed bg-muted/20 px-4 py-8 text-center">
          <Inbox class="size-8 text-muted-foreground" />
          <h3 class="mt-3 text-sm font-medium">{{ t('competitions.empty') }}</h3>
          <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
          <Button variant="outline" size="sm" class="mt-4" @click="loadCompetitions">
            {{ t('common.refresh') }}
          </Button>
        </div>

        <div v-else-if="isState('filtered-empty')" class="flex min-h-40 flex-col items-center justify-center rounded-md border border-dashed bg-muted/20 px-4 py-8 text-center">
          <Search class="size-8 text-muted-foreground" />
          <h3 class="mt-3 text-sm font-medium">{{ t('common.noResults') }}</h3>
          <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
          <Button variant="outline" size="sm" class="mt-4" @click="resetFilters">
            {{ t('common.reset') }}
          </Button>
        </div>

        <div v-else-if="isState('ready')" v-auto-animate class="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-3">
          <Card
            v-for="comp in state.visibleCompetitions"
            :key="comp.id"
            class="group h-full overflow-hidden transition-all duration-300 hover:-translate-y-1 hover:border-primary/30 hover:shadow-[0_24px_70px_rgb(37_99_235/0.14)]"
          >
            <CardHeader class="pb-3">
              <div class="mb-3 flex items-center gap-2">
                <Badge :variant="statusVariant(comp.status)" class="font-semibold">
                  {{ comp.status }}
                </Badge>
                <Badge variant="secondary" class="bg-blue-50 text-blue-700">
                  {{ comp.gameModeType || 'CTF' }}
                </Badge>
              </div>
              <div class="flex items-start justify-between gap-2">
                <CardTitle class="text-xl font-bold transition-colors group-hover:text-primary">
                  {{ comp.title }}
                </CardTitle>
              </div>
              <CardDescription v-if="comp.description" class="line-clamp-2 mt-2 leading-relaxed">
                {{ comp.description }}
              </CardDescription>
            </CardHeader>
            <CardContent class="space-y-4 pt-0">
              <div class="space-y-2 border-t pt-4 text-sm text-muted-foreground">
                <div class="flex items-center gap-2">
                  <Calendar class="size-3.5" />
                  <span>{{ formatDate(comp.startTime) }} ~ {{ formatDate(comp.endTime) }}</span>
                </div>
              </div>

              <div class="grid gap-2 sm:grid-cols-2">
                <Button variant="outline" as-child>
                  <RouterLink :to="`/competitions/${comp.id}/register`">
                    {{ t('teams.registerForCompetition') }}
                  </RouterLink>
                </Button>
                <Button as-child>
                  <RouterLink :to="`/competitions/${comp.id}`">
                    {{ t('competitions.enter') }}
                    <ArrowRight class="size-4" />
                  </RouterLink>
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  </AppLayout>
</template>

<style scoped>
.group:hover .card {
  border-color: var(--primary);
}
</style>
