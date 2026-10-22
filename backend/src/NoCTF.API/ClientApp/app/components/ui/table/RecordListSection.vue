<script setup lang="ts">
import type { MessageKey } from '../../../locales/en'
import { Alert, AlertDescription } from '../alert'
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '../empty'
import AdminLoadMore from '../load-more/AdminLoadMore.vue'
import { Skeleton } from '../skeleton'
import Table from './Table.vue'
import TableBody from './TableBody.vue'
import TableHeader from './TableHeader.vue'
import TableRow from './TableRow.vue'

/**
 * Shared state shell for paginated administration tables: failure alert,
 * loading skeleton, empty state, table surface and the cursor load-more footer.
 * Column headers and rows stay with the owning view through slots, so this only
 * owns the presentation states every such list repeats.
 */
withDefaults(defineProps<{
  loading: boolean
  errorMessage: string | null
  initialized: boolean
  itemCount: number
  hasMore: boolean
  emptyTitleKey: MessageKey
  emptyDescriptionKey: MessageKey
  skeletonCount?: number
  skeletonClass?: string
}>(), {
  skeletonCount: 8,
  skeletonClass: 'h-8 w-full',
})

const emit = defineEmits<{ loadMore: [] }>()
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <Alert v-if="errorMessage" variant="destructive">
      <AlertDescription>{{ $message(errorMessage) }}</AlertDescription>
    </Alert>

    <div v-if="loading && itemCount === 0" class="flex flex-col gap-3">
      <Skeleton v-for="i in skeletonCount" :key="i" :class="skeletonClass" />
    </div>

    <Empty v-else-if="!errorMessage && initialized && itemCount === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t(emptyTitleKey) }}</EmptyTitle>
        <EmptyDescription>{{ $t(emptyDescriptionKey) }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <template v-else-if="itemCount > 0">
      <Table class="table-fixed [&_th]:px-4 [&_th]:py-3 [&_td]:px-4 [&_td]:py-4">
        <TableHeader class="bg-muted/30"><TableRow><slot name="header" /></TableRow></TableHeader>
        <TableBody><slot name="rows" /></TableBody>
      </Table>
    </template>

    <AdminLoadMore :loading="loading" :has-more="hasMore" @load="emit('loadMore')" />
  </div>
</template>
