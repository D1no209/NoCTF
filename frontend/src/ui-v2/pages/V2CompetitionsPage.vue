<script setup lang="ts">
import { computed } from 'vue'
import { Blocks } from 'lucide-vue-next'
import CommandCompetitionCatalog from '../components/CommandCompetitionCatalog.vue'
import type { CommandCompetition } from '../components/CommandCompetitionCard.vue'
import CommandPageHeader from '../components/CommandPageHeader.vue'
import { useCompetitionsPage } from '@/features/competitions/useCompetitionsPage'

const {
  state,
  resetFilters,
  loadCompetitions,
  goCompetitionDetail,
  goCompetitionRegister,
} = useCompetitionsPage()

const visibleCompetitions = computed<CommandCompetition[]>(() => state.visibleCompetitions.map(competition => ({
  id: competition.id ?? '',
  title: competition.title ?? '',
  description: competition.description,
  status: competition.status ?? '',
  gameModeType: competition.gameModeType,
  startTime: competition.startTime ?? '',
  endTime: competition.endTime ?? '',
  registeredTeamCount: competition.registeredTeamCount,
})))

const competitionCount = computed(() => state.competitions.length)
const refreshing = computed(() => state.loadState === 'loading')
</script>

<template>
  <section class="v2-competitions">
    <CommandPageHeader
      signal-label="Competition service / live contract data"
      signal-tone="success"
      title="Competition registry"
      description="Filter and enter the competitions exposed by the platform service."
      :stat-icon="Blocks"
      :stat-value="String(competitionCount).padStart(2, '0')"
      stat-label="records"
    />

    <CommandCompetitionCatalog
      v-model:search="state.search"
      v-model:status-filter="state.statusFilter"
      v-model:mode-filter="state.modeFilter"
      :competitions="visibleCompetitions"
      :state="state.loadState"
      :refreshing="refreshing"
      @refresh="loadCompetitions"
      @reset="resetFilters"
      @open="goCompetitionDetail"
      @register="goCompetitionRegister"
    />
  </section>
</template>

<style scoped>
.v2-competitions { display: grid; gap: 18px; }
</style>
