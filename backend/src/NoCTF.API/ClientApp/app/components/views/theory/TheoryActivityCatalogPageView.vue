<script setup lang="ts">
import { toRefs } from 'vue'
import type { TheoryActivityCatalogState } from '~/features/theory/useTheoryActivityCatalogPage'
const props = defineProps<{ state: TheoryActivityCatalogState }>()
const { state } = toRefs(props)
</script>
<template>
  <section class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-2xl font-semibold">{{ $t('theory.activities') }}</h1><div class="flex gap-2"><Button v-if="state.staff" variant="outline" @click="state.participate">{{ $t('theory.activities') }}</Button><Button v-if="state.canOrganize && !state.staff" variant="outline" @click="state.manage">{{ $t('theory.settings') }}</Button><Button v-if="state.canOrganize && state.staff" @click="state.create">{{ $t('theory.newActivity') }}</Button></div></div>
    <Input v-model="state.search" :placeholder="$t('theory.search')" :aria-label="$t('theory.search')" class="max-w-xl" />
    <Alert v-if="state.error" variant="destructive"><AlertDescription>{{ $message(state.error) }}</AlertDescription></Alert>
    <Skeleton v-if="state.loading && !state.rows.length" class="h-64" />
    <div v-else class="flex min-w-0 flex-col gap-4"><Table><TableHeader><TableRow><TableHead>{{ $t('theory.title') }}</TableHead><TableHead>{{ $t('theory.kind') }}</TableHead><TableHead>{{ $t('theory.state') }}</TableHead><TableHead>{{ $t('theory.scoring') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody><TableRow v-for="row in state.rows" :key="row.id"><TableCell>{{ row.name }}</TableCell><TableCell>{{ row.kind === 0 ? $t('theory.practice') : $t('theory.examination') }}</TableCell><TableCell>{{ state.activityStateLabel(row.state) }}</TableCell><TableCell>{{ state.scoringLabel(row.scoringMode) }}</TableCell><TableCell><Button v-if="state.staff" variant="ghost" @click="state.open(row)">{{ $t('theory.settings') }}</Button><Button v-else-if="state.sessions[row.id!]?.canAnswer" variant="ghost" @click="state.open(row)">{{ $t('theory.continue') }}</Button><Button v-else-if="state.sessions[row.id!]?.canIssue" @click="state.open(row, true)">{{ $t('theory.start') }}</Button><Button v-else variant="ghost" @click="state.openSummary(row)">{{ $t('theory.submitted') }}</Button></TableCell></TableRow></TableBody></Table>
      <Empty v-if="!state.rows.length"><EmptyHeader><EmptyTitle>{{ $t('theory.empty') }}</EmptyTitle></EmptyHeader></Empty>
      <OffsetPagination :page="state.page" :page-count="state.pageCount" :total="state.total" :limit="state.limit" :loading="state.loading" @update:page="state.setPage" @update:limit="state.setLimit" />
    </div>
    <Dialog :open="state.summaryOpen" @update:open="state.closeSummary"><DialogContent class="max-w-2xl"><DialogHeader><DialogTitle>{{ state.summaryActivity?.name }}</DialogTitle></DialogHeader>
      <div class="flex flex-col gap-3 text-sm">
        <p>{{ $t('theory.paperSummary', { count: state.summaryActivity?.questionCount ?? 0, points: state.summaryActivity?.maximumPaperPoints ?? 0, minutes: Math.round((state.summaryActivity?.durationSeconds ?? 0) / 60) }) }}</p>
        <p v-if="state.summaryActivity?.competitionId" class="text-muted-foreground">{{ $t('theory.shared') }}</p>
        <p class="text-muted-foreground">{{ state.activityStateLabel(state.summaryActivity?.state) }}</p>
        <div v-if="state.summaryActivity?.id && state.sessions[state.summaryActivity.id]?.paper" class="flex flex-wrap gap-8">
          <div><p class="text-muted-foreground">{{ $t('theory.score') }}</p><p class="text-3xl font-semibold tabular-nums">{{ state.sessions[state.summaryActivity.id]?.paper?.score ?? 0 }}</p></div>

        </div>
        <p class="text-muted-foreground">{{ $t('theory.attemptSummary', { used: state.summaryActivity?.id ? state.sessions[state.summaryActivity.id]?.issuedCount ?? 0 : 0, limit: state.summaryActivity?.attemptLimit ?? 1 }) }}</p>
      </div>
      <DialogFooter><Button variant="outline" @click="state.closeSummary">{{ $t('common.action.close') }}</Button></DialogFooter>
    </DialogContent></Dialog>
  </section>
</template>
