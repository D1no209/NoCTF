<script setup lang="ts">
import { toRefs } from 'vue'
import type { TheoryBankPageState } from '~/features/theory/useTheoryBankPage'
const props = defineProps<{ state: TheoryBankPageState }>()
const { state } = toRefs(props)
</script>
<template>
  <section class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-2xl font-semibold">{{ $t('theory.bank') }}</h1><Button v-if="state.canOrganize" @click="state.create">{{ $t('theory.newQuestion') }}</Button></div>
    <Input v-model="state.search" :aria-label="$t('theory.search')" :placeholder="$t('theory.search')" class="max-w-xl" />
    <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
      <Select v-model="state.kind"><SelectTrigger :aria-label="$t('theory.kind')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem value="all">{{ $t('theory.allKinds') }}</SelectItem><SelectItem v-for="kind in state.kinds" :key="kind" :value="String(kind)">{{ state.questionKindLabel(kind) }}</SelectItem></SelectGroup></SelectContent></Select>
      <Select v-model="state.status"><SelectTrigger :aria-label="$t('theory.state')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem value="all">{{ $t('theory.allStates') }}</SelectItem><SelectItem v-for="value in state.kinds" :key="value" :value="String(value)">{{ state.questionStateLabel(value) }}</SelectItem></SelectGroup></SelectContent></Select>
      <Select v-model="state.difficulty"><SelectTrigger :aria-label="$t('theory.difficulty')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem value="all">{{ $t('theory.anyDifficulty') }}</SelectItem><SelectItem v-for="value in state.kinds" :key="value" :value="String(value)">{{ $t(state.difficultyLabel(value)) }}</SelectItem></SelectGroup></SelectContent></Select>
      <Input v-model="state.knowledge" :aria-label="$t('theory.filterKnowledge')" :placeholder="$t('theory.filterKnowledge')" />
      <Input v-model="state.tag" :aria-label="$t('theory.filterTag')" :placeholder="$t('theory.filterTag')" />
    </div>
    <Alert v-if="state.error" variant="destructive"><AlertDescription>{{ $message(state.error) }}</AlertDescription></Alert>
    <Skeleton v-if="state.loading && !state.rows.length" class="h-64" />
    <Card v-else><CardContent class="pt-6"><Table><TableHeader><TableRow><TableHead>{{ $t('theory.title') }}</TableHead><TableHead>{{ $t('theory.kind') }}</TableHead><TableHead>{{ $t('theory.state') }}</TableHead><TableHead>{{ $t('theory.difficulty') }}</TableHead><TableHead>{{ $t('theory.classifications') }}</TableHead><TableHead>{{ $t('theory.exposure') }}</TableHead><TableHead>{{ $t('theory.references') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody>
      <TableRow v-for="row in state.rows" :key="row.id"><TableCell>{{ row.title }}</TableCell><TableCell>{{ state.questionKindLabel(row.kind) }}</TableCell><TableCell><Badge variant="secondary">{{ state.questionStateLabel(row.state) }}</Badge></TableCell><TableCell>{{ $t(state.difficultyLabel(row.difficulty)) }}</TableCell><TableCell>{{ state.classifications(row) }}</TableCell><TableCell>{{ row.firstExposedAt ? $t('theory.exposed') : $t('theory.unexposed') }}</TableCell><TableCell>{{ row.paperReferences ?? 0 }}</TableCell><TableCell><Button v-if="row.canReadAnswers" variant="ghost" @click="state.open(row)">{{ $t('theory.edit') }}</Button><span v-else class="text-sm text-muted-foreground">{{ $t('theory.use') }}</span></TableCell></TableRow>
    </TableBody></Table><Empty v-if="!state.rows.length"><EmptyHeader><EmptyTitle>{{ $t('theory.empty') }}</EmptyTitle></EmptyHeader></Empty>
    <OffsetPagination :page="state.page" :page-count="state.pageCount" :total="state.total" :limit="state.limit" :loading="state.loading" @update:page="state.setPage" @update:limit="state.setLimit" />
    </CardContent></Card>
  </section>
</template>
