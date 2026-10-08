<script setup lang="ts">
import { toRefs } from 'vue'
import type { MySingleWriteUpsState } from '~/features/writeups/useMySingleWriteUps'
const props = defineProps<{ state: MySingleWriteUpsState }>()
const { loading, error, search, rows, load, open } = toRefs(props.state)
</script>
<template>
  <Card><CardHeader><div class="flex flex-wrap items-center justify-between gap-3"><CardTitle>{{ $t('challengeWriteUp.title') }}</CardTitle><Input v-model="search" class="max-w-72" :placeholder="$t('challengeWriteUp.search')" :aria-label="$t('challengeWriteUp.search')" /></div></CardHeader><CardContent>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Skeleton v-if="loading" class="h-72 w-full" />
    <Table v-else-if="rows.length"><TableHeader><TableRow><TableHead>{{ $t('writeUp.challenge') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead><TableHead class="text-right" /></TableRow></TableHeader><TableBody><TableRow v-for="row in rows" :key="row.challenge.id"><TableCell class="font-medium">{{ row.challenge.title }}</TableCell><TableCell><Badge variant="secondary">{{ $t(row.statusKey) }}</Badge></TableCell><TableCell class="text-right"><Button size="sm" variant="outline" :disabled="row.challenge.locked" @click="open(row.challenge.id)">{{ $t('challengeWriteUp.write') }}</Button></TableCell></TableRow></TableBody></Table>
    <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('challengeWriteUp.noChallenges') }}</EmptyTitle></EmptyHeader></Empty>
    <Button v-if="error" variant="ghost" @click="load">{{ $t('common.label.retry') }}</Button>
  </CardContent></Card>
</template>
