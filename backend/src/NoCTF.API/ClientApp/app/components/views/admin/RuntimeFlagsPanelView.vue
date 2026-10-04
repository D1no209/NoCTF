<script setup lang="ts">
import { toRefs } from 'vue'
import type { RuntimeFlagsPanelViewState } from '~/features/admin/useRuntimeFlagsPanel'
const viewProps = defineProps<{ state: RuntimeFlagsPanelViewState }>()
const { queried, includeHistory, rows, load, toggle, copy, adminFormatDateTime, loading, error, page, pageCount, total, pageLimit, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex flex-col gap-3">
    <Separator />
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 class="text-sm font-semibold">{{ $t('runtimeFlags.title') }}</h3>
      <Button type="button" variant="outline" size="sm" :disabled="loading" @click="load">
        <Spinner v-if="loading" data-icon="inline-start" />{{ queried ? $t('common.label.refresh') : $t('runtimeFlags.query') }}
      </Button>
    </div>
    <Field orientation="horizontal">
      <Switch v-model="includeHistory" :aria-label="$t('runtimeFlags.history')" />
      <span class="text-sm">{{ $t('runtimeFlags.history') }}</span>
    </Field>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Skeleton v-if="loading && !rows.length" class="h-24 w-full" />
    <Empty v-else-if="queried && !rows.length && !error" class="py-4">
      <EmptyHeader><EmptyTitle>{{ $t('runtimeFlags.empty') }}</EmptyTitle></EmptyHeader>
    </Empty>
    <template v-else-if="queried">
      <div v-for="row in rows" :key="row.flag?.id" class="flex flex-col gap-2 py-2">
        <div class="flex flex-wrap items-center gap-2">
          <Badge variant="secondary">{{ translate(row.sourceKey) }}</Badge>
          <Badge :variant="row.state === 'Active' ? 'default' : 'outline'">{{ translate(row.stateKey) }}</Badge>
          <Badge variant="outline">{{ translate(row.matchKey) }}</Badge>
          <span v-if="row.round !== null" class="text-xs">{{ $t('runtimeFlags.round', { round: row.round }) }}</span>
        </div>
        <ScrollSurface as="code" axis="x" class="font-mono text-xs">{{ row.revealed ? row.flag?.flag : '••••••••' }}</ScrollSurface>
        <div class="flex flex-wrap items-center gap-2">
          <Button type="button" variant="ghost" size="sm" @click="toggle(row)">{{ row.revealed ? $t('administration.label.hide') : $t('administration.label.showFullFlag') }}</Button>
          <Button type="button" variant="ghost" size="sm" @click="copy(row)">{{ $t('common.action.copy') }}</Button>
        </div>
        <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-xs">
          <dt class="text-muted-foreground">{{ $t('common.label.creationTime') }}</dt><dd>{{ adminFormatDateTime(row.flag?.createdAt) }}</dd>
          <dt class="text-muted-foreground">{{ $t('runtimeFlags.validFrom') }}</dt><dd>{{ adminFormatDateTime(row.flag?.validStart) }}</dd>
          <dt class="text-muted-foreground">{{ $t('runtimeFlags.validUntil') }}</dt><dd>{{ adminFormatDateTime(row.flag?.validUntil) }}</dd>
          <template v-if="row.flag?.deletedAt"><dt class="text-muted-foreground">{{ $t('runtimeFlags.deletedAt') }}</dt><dd>{{ adminFormatDateTime(row.flag.deletedAt) }}</dd></template>
        </dl>
        <FieldDescription v-if="row.source === 'AwdRound'">{{ $t('runtimeFlags.roundApplicability') }}</FieldDescription>
      </div>
      <OffsetPagination :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading" @update:page="loadPage" @update:limit="setPageSize" />
    </template>
  </section>
</template>
