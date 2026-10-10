<script setup lang="ts">
defineProps<{
  page: number
  count: number
  limit: number
  hasPrevious: boolean
  hasNext: boolean
  loading: boolean
}>()
const emit = defineEmits<{ 'update:page': [number]; 'update:limit': [number] }>()
function selectLimit(value: unknown): void {
  const parsed = Number(value)
  if ([20, 50, 100, 200].includes(parsed)) emit('update:limit', parsed)
}
</script>

<template>
  <nav class="flex flex-wrap items-center justify-between gap-3 pt-4" :aria-label="$t('pagination.cursor.navigation')" aria-live="polite">
    <span class="text-sm text-muted-foreground">{{ $t('pagination.cursor.showing', { count }) }}</span>
    <div class="flex flex-wrap items-center gap-2">
      <Select :model-value="String(limit)" :disabled="loading" @update:model-value="selectLimit">
        <SelectTrigger class="h-9 w-24" :aria-label="$t('common.label.itemsPage')"><SelectValue /></SelectTrigger>
        <SelectContent><SelectGroup>
          <SelectItem v-for="size in [20, 50, 100, 200]" :key="size" :value="String(size)">{{ size }}</SelectItem>
        </SelectGroup></SelectContent>
      </Select>
      <Button variant="outline" size="sm" :disabled="loading || !hasPrevious" @click="emit('update:page', page - 1)">{{ $t('common.label.previousPage') }}</Button>
      <span class="px-2 text-sm tabular-nums" aria-current="page">{{ $t('pagination.cursor.page', { page }) }}</span>
      <Button variant="outline" size="sm" :disabled="loading || !hasNext" @click="emit('update:page', page + 1)">
        <Spinner v-if="loading" data-icon="inline-start" />{{ $t('common.label.nextPage') }}
      </Button>
    </div>
  </nav>
</template>
