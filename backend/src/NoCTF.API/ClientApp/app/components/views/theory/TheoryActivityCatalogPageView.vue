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
    <Card v-else><CardContent class="pt-6"><Table><TableHeader><TableRow><TableHead>{{ $t('theory.title') }}</TableHead><TableHead>{{ $t('theory.kind') }}</TableHead><TableHead>{{ $t('theory.state') }}</TableHead><TableHead>{{ $t('theory.scoring') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody><TableRow v-for="row in state.rows" :key="row.id"><TableCell>{{ row.name }}</TableCell><TableCell>{{ row.kind === 0 ? $t('theory.practice') : $t('theory.examination') }}</TableCell><TableCell>{{ state.activityStateLabel(row.state) }}</TableCell><TableCell>{{ state.scoringLabel(row.scoringMode) }}</TableCell><TableCell><Button variant="ghost" @click="state.open(row)">{{ state.staff ? $t('theory.settings') : $t('theory.continue') }}</Button></TableCell></TableRow></TableBody></Table>
      <Empty v-if="!state.rows.length"><EmptyHeader><EmptyTitle>{{ $t('theory.empty') }}</EmptyTitle></EmptyHeader></Empty>
      <OffsetPagination :page="state.page" :page-count="state.pageCount" :total="state.total" :limit="state.limit" :loading="state.loading" @update:page="state.setPage" @update:limit="state.setLimit" />
    </CardContent></Card>
  </section>
</template>
