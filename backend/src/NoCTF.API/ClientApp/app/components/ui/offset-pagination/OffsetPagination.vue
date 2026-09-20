<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  page: number
  pageCount: number
  total: number
  limit: number
  loading?: boolean
}>(), { loading: false })

const emit = defineEmits<{
  'update:page': [value: number]
  'update:limit': [value: number]
}>()

const visiblePages = computed(() => {
  const count = props.pageCount
  const current = props.page
  if (count <= 7) return Array.from({ length: count }, (_, index) => index + 1)
  if (current <= 4) return [1, 2, 3, 4, 5, count]
  if (current >= count - 3) return [1, count - 4, count - 3, count - 2, count - 1, count]
  return [1, current - 1, current, current + 1, count]
})

function selectLimit(value: unknown): void {
  const parsed = Number(value)
  if (Number.isInteger(parsed) && parsed > 0) emit('update:limit', parsed)
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3 pt-4" aria-live="polite">
    <span class="text-sm text-muted-foreground">
      {{ $t('ui.total') }}: {{ total }}
    </span>
    <div class="flex flex-wrap items-center gap-2">
      <Select :model-value="String(limit)" :disabled="loading" @update:model-value="selectLimit">
        <SelectTrigger class="h-9 w-24" :aria-label="$t('ui.itemsPerPage')"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem v-for="size in [10, 20, 50, 100]" :key="size" :value="String(size)">{{ size }}</SelectItem>
        </SelectContent>
      </Select>
      <Button
        variant="outline"
        size="sm"
        :disabled="loading || page <= 1"
        :aria-label="$t('ui.previousPage')"
        @click="emit('update:page', page - 1)"
      >{{ $t('ui.previousPage') }}</Button>
      <template v-for="(item, index) in visiblePages" :key="`${item}-${index}`">
        <span v-if="index > 0 && item - visiblePages[index - 1]! > 1" class="px-1 text-muted-foreground">…</span>
        <Button
          variant="ghost"
          size="sm"
          :disabled="loading"
          :data-active="item === page"
          :aria-current="item === page ? 'page' : undefined"
          @click="emit('update:page', item)"
        >{{ item }}</Button>
      </template>
      <Button
        variant="outline"
        size="sm"
        :disabled="loading || page >= pageCount"
        :aria-label="$t('ui.nextPage')"
        @click="emit('update:page', page + 1)"
      >{{ $t('ui.nextPage') }}</Button>
    </div>
  </div>
</template>
