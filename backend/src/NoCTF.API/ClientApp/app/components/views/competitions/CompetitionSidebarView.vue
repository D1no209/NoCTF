<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionSidebarViewState } from '~/features/competitions/useCompetitionSidebar'

const props = defineProps<{ state: CompetitionSidebarViewState }>()
const { EyeOff, loading, options, selectedId, group, counts, showDeleted, setGroup, select, LifecycleBadge } = toRefs(props.state)
</script>

<template>
  <ChoiceSidebar :items="options" :model-value="selectedId" :loading="loading" :label="$t('competitionBrowser.list')" :loading-label="$t('competitions.label.loadingRecentCompetitions')" :empty-label="$t('competitionBrowser.emptyGroup')" controls="competition-browser-detail" @update:model-value="select">
    <template #header>
    <header class="flex flex-col gap-3">
      <h2 class="text-base font-semibold">{{ $t('competitionBrowser.list') }}</h2>
      <ToggleGroup type="single" orientation="horizontal" :model-value="group" :disabled="loading" class="competition-status-switch" :aria-label="$t('competitionBrowser.group')" @update:model-value="setGroup">
        <ToggleGroupItem value="running">{{ $t('competitions.label.running', { count: counts.running }) }}</ToggleGroupItem>
        <ToggleGroupItem value="upcoming">{{ $t('competitions.label.upcoming', { count: counts.upcoming }) }}</ToggleGroupItem>
        <ToggleGroupItem value="finished">{{ $t('competitions.label.finished', { count: counts.finished }) }}</ToggleGroupItem>
        <ToggleGroupItem v-if="showDeleted" value="deleted">{{ $t('competitionBrowser.deleted', { count: counts.deleted }) }}</ToggleGroupItem>
      </ToggleGroup>
    </header>
    </template>
      <template #item="{ item }">
        <span class="relative isolate flex min-w-0 w-full flex-col gap-3">
          <TypeWatermark :text="gameModeLabel(item.competition.mode)" size="compact" class="text-primary" />
          <span class="sr-only">{{ gameModeLabel(item.competition.mode) }}</span>
          <span class="relative z-10 flex flex-wrap items-center gap-2">
            <Badge v-if="item.competition.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
            <component :is="LifecycleBadge" v-else :status="item.competition.status ?? undefined" />
            <Badge v-if="item.competition.accessMode === 'StaffOnly'" variant="secondary">
              <EyeOff class="size-3" />{{ $t('competitionAccess.badge') }}
            </Badge>
          </span>
          <span class="relative z-10 break-words text-sm font-semibold">{{ item.label }}</span>
          <span class="relative z-10 text-xs font-normal text-muted-foreground">{{ formatDateTime(item.competition.startTime) }}</span>
        </span>
      </template>
  </ChoiceSidebar>
</template>
