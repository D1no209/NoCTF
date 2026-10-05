<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsIndexPageViewState } from '~/features/routes/competitions/useCompetitionsIndexPage'

const viewProps = defineProps<{ state: CompetitionsIndexPageViewState }>()
const { Plus, isAdministrator, loading, error, load, counts, group, selected, selectedId, missing, options, selectCompetition, createOpen, openCreateDialog, setCreateOpen, handleCompetitionCreated, CompetitionOverview, CompetitionSidebar, CreateCompetitionDialog } = toRefs(viewProps.state)
</script>

<template>
  <div data-contained-workspace-page class="competition-browser-page mx-auto flex w-full flex-col gap-6 py-8">
    <header class="flex items-center justify-between gap-4">
      <h1 class="text-display text-2xl">{{ $t('common.label.competitions') }}</h1>
      <Button v-if="isAdministrator" @click="openCreateDialog">
        <Plus data-icon="inline-start" />{{ $t('competitions.label.newCompetition') }}
      </Button>
    </header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}<Button variant="outline" size="sm" @click="load">{{ $t('common.label.retry') }}</Button></AlertDescription></Alert>
    <div class="competition-browser-layout">
      <div class="min-w-0">
        <component :is="CompetitionSidebar" v-model:group="group" :loading="loading" :options="options" :selected-id="selectedId" :counts="counts" :show-deleted="isAdministrator && counts.deleted > 0" @select="selectCompetition" />
      </div>
      <section id="competition-browser-detail" class="min-h-0 min-w-0" :aria-label="$t('competitionBrowser.details')" :aria-busy="loading">
        <Skeleton v-if="loading" class="h-96 w-full" />
        <Card v-else-if="selected" class="competition-overview-card h-full min-h-0 overflow-hidden gap-0 py-0">
          <MotionSwap :identity="selectedId || ''" preset="film-up">
            <component :is="CompetitionOverview" :key="selected.id" :competition="selected" @audience-changed="load" />
          </MotionSwap>
        </Card>
        <Empty v-else class="min-h-72 border"><EmptyHeader><EmptyTitle>{{ missing ? $t('competitionBrowser.notFound') : $t('competitionBrowser.choose') }}</EmptyTitle><EmptyDescription>{{ $t('competitionBrowser.selectionHint') }}</EmptyDescription></EmptyHeader></Empty>
      </section>
    </div>
    <component v-if="isAdministrator" :is="CreateCompetitionDialog" :open="createOpen" @update:open="setCreateOpen" @created="handleCompetitionCreated" />
  </div>
</template>
