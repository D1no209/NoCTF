<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { AlertCircle, ArrowRight, Calendar, Inbox, RotateCw, Search, Users } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import CompetitionListItem from '@/components/competitions/CompetitionListItem.vue'

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

const props = defineProps<{
  search: string
  statusFilter: string
  modeFilter: string
  visibleCompetitions: Competition[]
  loadState: LoadState
}>()

const emit = defineEmits<{
  'update:search': [value: string]
  'update:statusFilter': [value: string]
  'update:modeFilter': [value: string]
  refresh: []
  resetFilters: []
}>()

const { t } = useI18n()

const featured = computed(() =>
  props.visibleCompetitions.find(c => ['running', 'active'].includes(c.status.toLowerCase())),
)

const gridList = computed(() => {
  if (!featured.value)
    return props.visibleCompetitions
  return props.visibleCompetitions.filter(c => c.id !== featured.value!.id)
})

function modeLabel(mode?: string | null) {
  return (mode || 'CTF').toUpperCase()
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <div class="space-y-6">
    <!-- Filter bar -->
    <Card class="p-3">
      <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_180px_180px_auto]">
        <div class="relative">
          <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            :model-value="search"
            :placeholder="t('competitions.searchPlaceholder')"
            class="!bg-transparent pl-10 shadow-none"
            @update:model-value="emit('update:search', String($event))"
          />
        </div>

        <Select :model-value="statusFilter" @update:model-value="emit('update:statusFilter', String($event))">
          <SelectTrigger class="!bg-transparent shadow-none">
            <SelectValue :placeholder="t('common.status')" />
          </SelectTrigger>
          <SelectContent :body-lock="false" :disable-outside-pointer-events="false">
            <SelectItem value="all">{{ t('common.all') }} {{ t('common.status') }}</SelectItem>
            <SelectItem value="draft">{{ t('competitions.status.draft') }}</SelectItem>
            <SelectItem value="published">{{ t('competitions.status.published') }}</SelectItem>
            <SelectItem value="running">{{ t('competitions.status.running') }}</SelectItem>
            <SelectItem value="finished">{{ t('competitions.status.finished') }}</SelectItem>
          </SelectContent>
        </Select>

        <Select :model-value="modeFilter" @update:model-value="emit('update:modeFilter', String($event))">
          <SelectTrigger class="!bg-transparent shadow-none">
            <SelectValue :placeholder="t('common.mode')" />
          </SelectTrigger>
          <SelectContent :body-lock="false" :disable-outside-pointer-events="false">
            <SelectItem value="all">{{ t('common.all') }} {{ t('common.mode') }}</SelectItem>
            <SelectItem value="ctf">CTF</SelectItem>
            <SelectItem value="awd">AWD</SelectItem>
            <SelectItem value="awdp">AWDP</SelectItem>
            <SelectItem value="koh">KoH</SelectItem>
          </SelectContent>
        </Select>

        <Button variant="ghost" :disabled="loadState === 'loading'" @click="emit('refresh')">
          <RotateCw class="size-4" :class="{ 'animate-spin': loadState === 'loading' }" />
          {{ t('common.refresh') }}
        </Button>
      </div>
    </Card>

    <!-- Content -->
    <div class="min-h-40">
      <!-- Loading -->
      <div v-if="loadState === 'loading'" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
        <Skeleton v-for="i in 6" :key="i" class="h-80 border-2 border-border" />
      </div>

      <!-- Error -->
      <Card v-else-if="loadState === 'error'" class="flex min-h-60 flex-col items-center justify-center border-dashed px-4 py-8 text-center">
        <AlertCircle class="size-8 text-destructive animate-icon-pop" />
        <h3 class="mt-3 text-sm font-medium">{{ t('competitions.loadError') }}</h3>
        <Button variant="outline" size="sm" class="mt-4" @click="emit('refresh')">
          {{ t('common.refresh') }}
        </Button>
      </Card>

      <!-- Empty -->
      <Card v-else-if="loadState === 'empty'" class="flex min-h-60 flex-col items-center justify-center border-dashed px-4 py-8 text-center">
        <Inbox class="size-8 text-muted-foreground animate-icon-pop" />
        <h3 class="mt-3 text-sm font-medium">{{ t('competitions.empty') }}</h3>
        <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
        <Button variant="outline" size="sm" class="mt-4" @click="emit('refresh')">
          {{ t('common.refresh') }}
        </Button>
      </Card>

      <!-- Filtered empty -->
      <Card v-else-if="loadState === 'filtered-empty'" class="flex min-h-60 flex-col items-center justify-center border-dashed px-4 py-8 text-center">
        <Search class="size-8 text-muted-foreground animate-icon-pop" />
        <h3 class="mt-3 text-sm font-medium">{{ t('common.noResults') }}</h3>
        <p class="mt-1 max-w-sm text-sm text-muted-foreground">{{ t('competitions.emptyDescription') }}</p>
        <Button variant="outline" size="sm" class="mt-4" @click="emit('resetFilters')">
          {{ t('common.reset') }}
        </Button>
      </Card>

      <div v-else class="space-y-6">
        <!-- Featured running competition -->
        <Card v-if="featured">
          <Panel variant="dark" class="p-4 md:p-6">
            <div class="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
              <div class="min-w-0 space-y-2">
                <div class="flex flex-wrap items-center gap-2">
                  <Badge variant="default" class="animate-status-pulse">{{ t('competitions.status.running') }}</Badge>
                  <span class="text-xs font-bold uppercase tracking-[0.2em] text-[var(--awdp-text-inverse)]">{{ modeLabel(featured.gameModeType) }}</span>
                </div>
                <h2 class="text-2xl font-bold text-[var(--awdp-text-inverse)] md:text-3xl">{{ featured.title }}</h2>
                <p v-if="featured.description" class="line-clamp-2 text-sm text-[var(--awdp-text-inverse)] md:text-base">{{ featured.description }}</p>
                <div class="flex flex-wrap gap-x-4 text-xs text-[var(--awdp-text-muted)]">
                  <div class="flex items-center gap-1.5">
                    <Calendar class="size-3.5" />
                    <span>{{ formatDate(featured.startTime) }} ~ {{ formatDate(featured.endTime) }}</span>
                  </div>
                  <div class="flex items-center gap-1.5">
                    <Users class="size-3.5" />
                    <span>{{ featured.registeredTeamCount ?? 0 }} {{ t('competitions.registeredTeams') }}</span>
                  </div>
                </div>
              </div>
              <Button variant="secondary" size="lg" class="shrink-0" as-child>
                <RouterLink :to="`/competitions/${featured.id}`">
                  {{ t('competitions.enter') }}
                  <ArrowRight class="size-5" />
                </RouterLink>
              </Button>
            </div>
          </Panel>
        </Card>

        <!-- Cartridge grid -->
        <TransitionGroup
          name="shelf"
          tag="div"
          class="relative grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3"
        >
          <CompetitionListItem
            v-for="competition in gridList"
            :key="competition.id"
            :competition="competition"
          />
        </TransitionGroup>
      </div>
    </div>
  </div>
</template>
