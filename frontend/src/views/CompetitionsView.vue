<script setup lang="ts">
import { onMounted, reactive, watch } from 'vue'
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
import { AlertCircle, ArrowRight, Calendar, Inbox, RotateCw, Search, Users } from 'lucide-vue-next'

const { t } = useI18n()

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
  registeredTeamCount?: number | null
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

function formatDate(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}

function modeLabel(mode?: string | null) {
  return (mode || 'CTF').toUpperCase()
}
</script>

<template>
  <AppLayout>
    <div class="noctf-page">
      <div class="flex flex-col justify-between gap-4 md:flex-row md:items-end">
        <PageHeader
          :title="t('competitions.title')"
          :description="t('competitions.subtitle')"
          class="flex-1"
        />
      </div>

      <div class="noctf-filter-bar md:grid-cols-[minmax(0,1fr)_190px_190px_auto]">
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

        <Button variant="outline" @click="loadCompetitions()" :disabled="isState('loading')">
          <RotateCw class="size-4" :class="{ 'animate-spin': isState('loading') }" />
          {{ t('common.refresh') }}
        </Button>
      </div>

      <div class="min-h-40">
        <div v-if="isState('loading')" class="grid gap-4">
          <Skeleton v-for="i in 4" :key="i" class="h-48 rounded-xl" />
        </div>

        <div v-else-if="isState('error')" class="noctf-state-box">
          <AlertCircle class="size-8 text-destructive" />
          <h3 class="mt-3 text-sm font-medium">{{ t('competitions.loadError') }}</h3>
          <Button variant="outline" size="sm" class="mt-4" @click="loadCompetitions">
            {{ t('common.refresh') }}
          </Button>
        </div>

        <div v-else-if="isState('empty')" class="noctf-state-box">
          <Inbox class="size-8 text-muted-foreground" />
          <h3 class="mt-3 text-sm font-medium">{{ t('competitions.empty') }}</h3>
          <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
          <Button variant="outline" size="sm" class="mt-4" @click="loadCompetitions">
            {{ t('common.refresh') }}
          </Button>
        </div>

        <div v-else-if="isState('filtered-empty')" class="noctf-state-box">
          <Search class="size-8 text-muted-foreground" />
          <h3 class="mt-3 text-sm font-medium">{{ t('common.noResults') }}</h3>
          <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
          <Button variant="outline" size="sm" class="mt-4" @click="resetFilters">
            {{ t('common.reset') }}
          </Button>
        </div>

        <div v-else-if="isState('ready')" class="competition-list">
          <article
            v-for="comp in state.visibleCompetitions"
            :key="comp.id"
            class="competition-row"
          >
            <div class="competition-poster" :data-mode="modeLabel(comp.gameModeType)">
              <span class="text-xs font-semibold uppercase tracking-[0.22em] text-white/70">{{ t('competitions.posterLabel') }}</span>
              <div>
                <p class="text-4xl font-black tracking-normal text-white md:text-5xl">{{ modeLabel(comp.gameModeType) }}</p>
                <p class="mt-2 line-clamp-2 text-sm font-medium text-white/72">{{ comp.title }}</p>
              </div>
            </div>

            <div class="competition-row-main">
              <div class="flex flex-wrap items-center gap-2">
                <Badge variant="outline" :class="statusToneClass(comp.status)">
                  {{ statusLabel(comp.status) }}
                </Badge>
                <Badge variant="secondary">
                  {{ modeLabel(comp.gameModeType) }}
                </Badge>
              </div>

              <div class="min-w-0">
                <h2 class="line-clamp-2 text-2xl font-bold leading-tight tracking-normal text-foreground md:text-3xl">
                  {{ comp.title }}
                </h2>
                <p v-if="comp.description" class="mt-3 line-clamp-3 max-w-5xl text-sm leading-6 text-muted-foreground md:text-base">
                  {{ comp.description }}
                </p>
              </div>

              <div class="competition-row-meta">
                <div class="flex items-center gap-2">
                  <Calendar class="size-4 text-primary" />
                  <span>{{ t('competitions.startLabel') }}{{ formatDate(comp.startTime) }}</span>
                </div>
                <div class="flex items-center gap-2">
                  <Calendar class="size-4 text-primary" />
                  <span>{{ t('competitions.endLabel') }}{{ formatDate(comp.endTime) }}</span>
                </div>
              </div>
            </div>

            <div class="competition-row-count">
              <Users class="size-5 text-primary" />
              <span class="text-3xl font-bold tracking-normal text-foreground">{{ comp.registeredTeamCount ?? 0 }}</span>
              <span class="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">{{ t('competitions.registeredTeams') }}</span>
            </div>

            <div class="competition-row-actions">
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
          </article>
        </div>
      </div>
    </div>
  </AppLayout>
</template>

<style scoped>
.competition-list {
  display: grid;
  gap: 1rem;
}

.competition-row {
  display: grid;
  grid-template-columns: minmax(13rem, 17rem) minmax(0, 1fr) minmax(8.5rem, 10rem) minmax(10rem, 12rem);
  min-height: 13rem;
  overflow: hidden;
  border: 1px solid oklch(0.87 0.025 255);
  background: oklch(1 0 0);
  border-radius: 0.85rem;
  box-shadow: 0 16px 45px rgb(15 23 42 / 0.055);
  transition:
    border-color 150ms ease,
    box-shadow 150ms ease,
    transform 150ms ease;
}

.competition-row:hover {
  border-color: oklch(0.78 0.075 262);
  box-shadow: 0 22px 55px rgb(79 70 229 / 0.11);
  transform: translateY(-1px);
}

.competition-poster {
  position: relative;
  isolation: isolate;
  display: flex;
  min-height: 13rem;
  flex-direction: column;
  justify-content: space-between;
  overflow: hidden;
  border-right: 1px solid oklch(0.87 0.025 255);
  background:
    linear-gradient(135deg, oklch(0.24 0.08 255), oklch(0.16 0.045 255)),
    oklch(0.19 0.05 255);
  padding: 1.35rem;
}

.competition-poster::before {
  position: absolute;
  inset: -35% auto auto 38%;
  z-index: -1;
  width: 14rem;
  height: 14rem;
  content: "";
  background: oklch(0.62 0.2 262 / 0.34);
  transform: rotate(24deg);
}

.competition-poster::after {
  position: absolute;
  right: 1rem;
  bottom: 0.5rem;
  z-index: -1;
  content: attr(data-mode);
  color: oklch(1 0 0 / 0.07);
  font-size: 5.5rem;
  font-weight: 900;
  line-height: 1;
}

.competition-row-main {
  display: flex;
  min-width: 0;
  flex-direction: column;
  justify-content: space-between;
  gap: 1.25rem;
  padding: 1.35rem 1.5rem;
}

.competition-row-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.85rem 1.25rem;
  color: oklch(0.42 0.02 255);
  font-size: 0.9rem;
}

.competition-row-count {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.35rem;
  border-left: 1px solid oklch(0.9 0.018 255);
  background: oklch(0.98 0.007 255);
  padding: 1.25rem;
}

.competition-row-actions {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.75rem;
  border-left: 1px solid oklch(0.9 0.018 255);
  padding: 1.25rem;
}

@media (max-width: 1120px) {
  .competition-row {
    grid-template-columns: minmax(11rem, 14rem) minmax(0, 1fr) minmax(9rem, 11rem);
  }

  .competition-row-actions {
    grid-column: 1 / -1;
    flex-direction: row;
    justify-content: flex-end;
    border-top: 1px solid oklch(0.9 0.018 255);
    border-left: 0;
  }
}

@media (max-width: 760px) {
  .competition-row {
    grid-template-columns: 1fr;
  }

  .competition-poster,
  .competition-row-count,
  .competition-row-actions {
    border-left: 0;
    border-right: 0;
  }

  .competition-poster,
  .competition-row-count {
    border-bottom: 1px solid oklch(0.9 0.018 255);
  }

  .competition-row-count {
    min-height: 7rem;
  }

  .competition-row-actions {
    justify-content: stretch;
  }

  .competition-row-actions > * {
    flex: 1 1 0;
  }
}
</style>
