<script setup lang="ts">
import { computed, onMounted, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
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
import { AlertCircle, ArrowRight, Calendar, Clock3, Inbox, ListFilter, RotateCw, Search } from 'lucide-vue-next'

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
  activeCompetitionId: '',
})

const activeCompetition = computed(() => {
  return state.visibleCompetitions.find((comp) => comp.id === state.activeCompetitionId) ?? state.visibleCompetitions[0] ?? null
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

  if (!state.visibleCompetitions.some((comp) => comp.id === state.activeCompetitionId)) {
    state.activeCompetitionId = state.visibleCompetitions[0]?.id ?? ''
  }
}

watch(() => [state.search, state.statusFilter, state.modeFilter], () => {
  if (state.loadState !== 'loading' && state.loadState !== 'error') {
    updateListState()
  }
})

function statusLabel(status: string) {
  const key = status.toLowerCase()
  return t(`competitions.status.${key}`, status)
}

function statusToneClass(status: string) {
  const key = status.toLowerCase()
  if (key === 'running' || key === 'active') return 'border-emerald-200 bg-emerald-50 text-emerald-700'
  if (key === 'published' || key === 'upcoming') return 'border-blue-200 bg-blue-50 text-blue-700'
  if (key === 'paused' || key === 'pending') return 'border-amber-200 bg-amber-50 text-amber-700'
  if (key === 'finished' || key === 'ended') return 'border-slate-200 bg-slate-50 text-slate-600'
  return 'border-violet-200 bg-violet-50 text-violet-700'
}

function statusDotClass(status: string) {
  const key = status.toLowerCase()
  if (key === 'running' || key === 'active') return 'bg-emerald-500'
  if (key === 'published' || key === 'upcoming') return 'bg-blue-500'
  if (key === 'paused' || key === 'pending') return 'bg-amber-500'
  if (key === 'finished' || key === 'ended') return 'bg-slate-400'
  return 'bg-violet-500'
}

function selectCompetition(id: string) {
  state.activeCompetitionId = id
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

        <div v-else-if="isState('ready')" class="competition-stage">
          <aside class="noctf-panel competition-queue-panel rounded-xl p-4">
            <div class="mb-4 flex items-center justify-between gap-3">
              <div>
                <p class="text-xs font-bold uppercase tracking-[0.05em] text-muted-foreground">{{ t('competitions.deckTitle') }}</p>
                <p class="mt-1 text-sm font-medium text-foreground">{{ t('competitions.visibleCount', { count: state.visibleCompetitions.length }) }}</p>
              </div>
              <ListFilter class="size-5 text-primary" />
            </div>

            <div v-auto-animate class="space-y-2">
              <button
                v-for="comp in state.visibleCompetitions"
                :key="comp.id"
                type="button"
                class="competition-queue-item w-full text-left"
                :class="{ 'is-active': activeCompetition?.id === comp.id }"
                @click="selectCompetition(comp.id)"
              >
                <span class="mt-1 size-2.5 shrink-0 rounded-full" :class="statusDotClass(comp.status)" />
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-sm font-semibold text-foreground">{{ comp.title }}</span>
                  <span class="mt-1 block truncate text-xs text-muted-foreground">
                    {{ statusLabel(comp.status) }} · {{ comp.gameModeType || 'CTF' }}
                  </span>
                </span>
              </button>
            </div>
          </aside>

          <section v-if="activeCompetition" class="competition-detail-panel">
            <div class="flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
              <div class="min-w-0 space-y-4">
                <div class="flex flex-wrap items-center gap-2">
                  <Badge variant="outline" :class="statusToneClass(activeCompetition.status)">
                    {{ statusLabel(activeCompetition.status) }}
                  </Badge>
                  <Badge variant="secondary" class="bg-secondary text-secondary-foreground">
                    {{ activeCompetition.gameModeType || 'CTF' }}
                  </Badge>
                </div>

                <div class="space-y-3">
                  <h2 class="text-2xl font-bold tracking-normal text-foreground">
                    {{ activeCompetition.title }}
                  </h2>
                  <p v-if="activeCompetition.description" class="max-w-3xl text-sm leading-6 text-muted-foreground">
                    {{ activeCompetition.description }}
                  </p>
                  <p v-else class="max-w-3xl text-sm leading-6 text-muted-foreground">
                    {{ t('competitions.emptyDescription') }}
                  </p>
                </div>
              </div>

              <div class="flex shrink-0 flex-col gap-2 sm:flex-row lg:flex-col">
                <Button variant="outline" as-child>
                  <RouterLink :to="`/competitions/${activeCompetition.id}/register`">
                    {{ t('teams.registerForCompetition') }}
                  </RouterLink>
                </Button>
                <Button as-child>
                  <RouterLink :to="`/competitions/${activeCompetition.id}`">
                    {{ t('competitions.enter') }}
                    <ArrowRight class="size-4" />
                  </RouterLink>
                </Button>
              </div>
            </div>

            <div class="competition-detail-meta">
              <div class="flex items-center gap-2">
                <Calendar class="size-4 text-primary" />
                <span>{{ t('competitions.startLabel') }}{{ formatDate(activeCompetition.startTime) }}</span>
              </div>
              <div class="flex items-center gap-2">
                <Clock3 class="size-4 text-primary" />
                <span>{{ t('competitions.endLabel') }}{{ formatDate(activeCompetition.endTime) }}</span>
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>
  </AppLayout>
</template>

<style scoped>
.competition-stage {
  display: grid;
  grid-template-columns: minmax(280px, 0.58fr) minmax(0, 1.42fr);
  gap: 1.5rem;
  align-items: stretch;
}

.competition-queue-panel {
  align-self: start;
}

.competition-queue-item {
  display: flex;
  gap: 0.75rem;
  border-radius: 0.75rem;
  border: 1px solid transparent;
  padding: 0.75rem;
  transition:
    border-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.competition-queue-item:hover,
.competition-queue-item:focus-visible,
.competition-queue-item.is-active {
  border-color: oklch(0.72 0.12 262 / 0.45);
  background: oklch(0.96 0.025 258);
}

.competition-detail-panel {
  border-radius: 1rem;
  border: 1px solid oklch(0.89 0.028 252);
  background: oklch(0.995 0.004 255 / 0.92);
  padding: 1.5rem;
  box-shadow: 0 22px 80px rgb(15 23 42 / 0.07);
}

.competition-detail-meta {
  margin-top: 1.5rem;
  display: grid;
  gap: 0.75rem;
  border-top: 1px solid oklch(0.9 0.023 252);
  padding-top: 1rem;
  color: oklch(0.49 0.05 260);
  font-size: 0.875rem;
}

@media (max-width: 1024px) {
  .competition-stage {
    grid-template-columns: 1fr;
  }

  .competition-detail-panel {
    order: 1;
  }
}
</style>
