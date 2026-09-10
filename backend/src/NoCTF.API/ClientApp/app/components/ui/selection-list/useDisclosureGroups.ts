import { ref, watch } from 'vue'

export interface DisclosureGroup { value: string; items: { value: string }[] }

/** Group expansion is presentation state and never changes the selected item. */
export function useDisclosureGroups(groups: () => DisclosureGroup[] | undefined, selected: () => string | null) {
  const closed = ref(new Set<string>())
  function setOpen(value: string, open: boolean) {
    const next = new Set(closed.value)
    if (open) next.delete(value)
    else next.add(value)
    closed.value = next
  }
  watch(selected, value => {
    const group = groups()?.find(group => group.items.some(item => item.value === value))
    if (group && closed.value.has(group.value)) setOpen(group.value, true)
  })
  return { isOpen: (value: string) => !closed.value.has(value), setOpen }
}
