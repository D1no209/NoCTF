<script setup lang="ts">
import { toRefs } from 'vue'
import type { MySingleWriteUpsState } from '~/features/writeups/useMySingleWriteUps'
const props = defineProps<{ state: MySingleWriteUpsState }>()
const { loading, error, search, status, statusOptions, filtered, rows, load, open, clearFilters } = toRefs(props.state)
</script>
<template>
  <Card><CardHeader class="gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3"><CardTitle>{{ $t('challengeWriteUp.title') }}</CardTitle><p v-if="!loading" class="text-sm text-muted-foreground" aria-live="polite">{{ $t('challengeWriteUp.matchingCount', { count: rows.length }) }}</p></div>
    <FieldGroup class="flex flex-col gap-3 sm:flex-row sm:items-end">
      <Field class="min-w-0 flex-1"><FieldLabel for="my-writeup-search" class="sr-only">{{ $t('challengeWriteUp.mySearch') }}</FieldLabel><Input id="my-writeup-search" v-model="search" :placeholder="$t('challengeWriteUp.mySearch')" /></Field>
      <Field class="w-full sm:w-44"><FieldLabel for="my-writeup-status" class="sr-only">{{ $t('challengeWriteUp.statusFilter') }}</FieldLabel><Select v-model="status"><SelectTrigger id="my-writeup-status" class="w-full" :aria-label="$t('challengeWriteUp.statusFilter')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ $t(option.label) }}</SelectItem></SelectGroup></SelectContent></Select></Field>
      <Button v-if="filtered" variant="ghost" @click="clearFilters">{{ $t('challengeWriteUp.clearFilters') }}</Button>
    </FieldGroup>
  </CardHeader><CardContent>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Skeleton v-if="loading" class="h-72 w-full" />
    <Table v-else-if="rows.length"><TableHeader><TableRow><TableHead>{{ $t('writeUp.challenge') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead><TableHead class="text-right" /></TableRow></TableHeader><TableBody><TableRow v-for="row in rows" :key="row.challenge.id"><TableCell class="font-medium">{{ row.challenge.title }}</TableCell><TableCell><Badge variant="secondary">{{ $t(row.statusKey) }}</Badge></TableCell><TableCell class="text-right"><Button size="sm" variant="outline" :disabled="row.challenge.locked" @click="open(row.challenge.id)">{{ $t('challengeWriteUp.write') }}</Button></TableCell></TableRow></TableBody></Table>
    <Empty v-else><EmptyHeader><EmptyTitle>{{ $t(filtered ? 'challengeWriteUp.noMatchingChallenges' : 'challengeWriteUp.noChallenges') }}</EmptyTitle><EmptyDescription v-if="filtered">{{ $t('challengeWriteUp.tryOtherFilters') }}</EmptyDescription></EmptyHeader><EmptyContent v-if="filtered"><Button variant="outline" @click="clearFilters">{{ $t('challengeWriteUp.clearFilters') }}</Button></EmptyContent></Empty>
    <Button v-if="error" variant="ghost" @click="load">{{ $t('common.label.retry') }}</Button>
  </CardContent></Card>
</template>
