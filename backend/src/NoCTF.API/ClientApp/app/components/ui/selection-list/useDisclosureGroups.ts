import { ref, watch } from 'vue'

export interface DisclosureGroup { value: string; items: { value: string }[] }

/** Group expansion is presentation state and never changes the selected item. */
export function useDisclosureGroups(groups: () => DisclosureGroup[] | undefined, selected: () => string | null) {
  const openGroups = ref(new Set<string>())
  function setOpen(value: string, nextOpen: boolean) {
    const next = new Set(openGroups.value)
    if (nextOpen) next.add(value)
    else next.delete(value)
    openGroups.value = next
  }
  watch([
    () => groups()?.map(group => `${group.value}:${group.items.map(item => item.value).join(',')}`).join('|') ?? '',
    selected,
  ], ([, value]) => {
    const group = groups()?.find(group => group.items.some(item => item.value === value))
    if (group && !openGroups.value.has(group.value)) setOpen(group.value, true)
  }, { immediate: true })
  return { isOpen: (value: string) => openGroups.value.has(value), setOpen }
}
