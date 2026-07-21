<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import AppLayout from '@/ui-v1/components/layout/AppLayout.vue'
import PageHeader from '@/ui-v1/components/layout/PageHeader.vue'
import CompetitionsExplorer from '@/ui-v1/components/competitions/CompetitionsExplorer.vue'
import { useCompetitionsPage } from '@/features/competitions/useCompetitionsPage'

const { t } = useI18n()

const { state, resetFilters, loadCompetitions } = useCompetitionsPage()

const visibleCompetitions = computed(() => state.visibleCompetitions.map(competition => ({
  id: competition.id ?? '',
  title: competition.title ?? '',
  description: competition.description,
  status: competition.status ?? '',
  gameModeType: competition.gameModeType,
  startTime: competition.startTime ?? '',
  endTime: competition.endTime ?? '',
  registeredTeamCount: competition.registeredTeamCount,
})))
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <div class="flex flex-col justify-between gap-4 md:flex-row md:items-end">
        <PageHeader
          :title="t('competitions.title')"
          :description="t('competitions.subtitle')"
          class="flex-1"
        />
      </div>

      <CompetitionsExplorer
        v-model:search="state.search"
        v-model:status-filter="state.statusFilter"
        v-model:mode-filter="state.modeFilter"
        :visible-competitions="visibleCompetitions"
        :load-state="state.loadState"
        @refresh="loadCompetitions"
        @reset-filters="resetFilters"
      />
    </div>
  </AppLayout>
</template>