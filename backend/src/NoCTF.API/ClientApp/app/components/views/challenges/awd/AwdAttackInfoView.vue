<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdAttackInfoViewState } from '~/features/challenges/awd/useAwdAttackInfo'
const props = defineProps<{ state: AwdAttackInfoViewState }>()
const { teamName, rows, loading, error, page, pageCount, total, limit, loadPage, setPageSize, refresh, setOpen } = toRefs(props.state)
</script>

<template>
  <Dialog :open="true" @update:open="setOpen">
    <DialogScrollContent class="sm:max-w-3xl">
      <DialogHeader>
        <DialogTitle>{{ $t('challenges.awdAttackInfo.title') }} · {{ teamName }}</DialogTitle>
        <DialogDescription>{{ $t('challenges.awdAttackInfo.notice') }}</DialogDescription>
      </DialogHeader>
      <Button variant="outline" size="sm" class="self-start" :disabled="loading" @click="refresh">{{ $t('common.label.refresh') }}</Button>
      <Alert v-if="error"><AlertDescription>{{ $message(error.displayMessage) }}</AlertDescription></Alert>
      <Skeleton v-else-if="loading && !rows.length" class="h-32" />
      <Empty v-else-if="!rows.length"><EmptyHeader><EmptyTitle>{{ $t('challenges.awdAttackInfo.empty') }}</EmptyTitle></EmptyHeader></Empty>
      <Table v-else>
        <TableHeader><TableRow>
          <TableHead>{{ $t('challenges.awdAttackInfo.generation') }}</TableHead>
          <TableHead>{{ $t('challenges.awdAttackInfo.locator') }}</TableHead>
          <TableHead>{{ $t('challenges.awdAttackInfo.validity') }}</TableHead>
        </TableRow></TableHeader>
        <TableBody><TableRow v-for="row in rows" :key="row.flagId!">
          <TableCell class="align-top"><div>{{ row.storeName }} · {{ row.ordinal }}</div><div class="text-muted-foreground">{{ $t('challenges.awdRules.round', { number: row.generationRound ?? 0 }) }}</div><div class="text-xs text-muted-foreground">{{ row.issuedText }}</div></TableCell>
          <TableCell class="max-w-sm align-top"><code class="whitespace-pre-wrap break-all">{{ row.publicLocator }}</code></TableCell>
          <TableCell class="align-top">{{ row.expiryText }}</TableCell>
        </TableRow></TableBody>
      </Table>
      <OffsetPagination :page="page" :page-count="pageCount" :total="total" :limit="limit" :loading="loading" @update:page="loadPage" @update:limit="setPageSize" />
    </DialogScrollContent>
  </Dialog>
</template>
