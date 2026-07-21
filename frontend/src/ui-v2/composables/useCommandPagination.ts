import { computed, ref, watch, type Ref } from 'vue'

export function useCommandPagination<T>(rows: Ref<T[]>, pageSize = 10) {
  const page = ref(0)

  const pageCount = computed(() => Math.max(1, Math.ceil(rows.value.length / pageSize)))

  const pagedRows = computed(() =>
    rows.value.slice(page.value * pageSize, (page.value + 1) * pageSize))

  watch(rows, () => {
    if (page.value > pageCount.value - 1)
      page.value = 0
  })

  return { page, pageCount, pagedRows }
}
