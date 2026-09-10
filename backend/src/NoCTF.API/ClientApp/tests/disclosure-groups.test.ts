import { expect, test } from 'bun:test'
import { effectScope, nextTick, ref } from 'vue'
import { useDisclosureGroups } from '../app/components/ui/selection-list/useDisclosureGroups'

test('groups open independently and collapsing one does not change the selected item', () => {
  const scope = effectScope()
  const selected = ref<string | null>('misc-a')
  const groups = [{ value: 'Misc', items: [{ value: 'misc-a' }] }, { value: 'Web', items: [{ value: 'web-a' }] }]
  const state = scope.run(() => useDisclosureGroups(() => groups, () => selected.value))!
  expect(state.isOpen('Misc')).toBe(true)
  expect(state.isOpen('Web')).toBe(true)
  state.setOpen('Misc', false)
  expect(state.isOpen('Misc')).toBe(false)
  expect(state.isOpen('Web')).toBe(true)
  expect(selected.value).toBe('misc-a')
  state.setOpen('Web', false)
  expect(state.isOpen('Web')).toBe(false)
  scope.stop()
})

test('navigating to an item reveals its group without changing unrelated groups', async () => {
  const scope = effectScope()
  const selected = ref<string | null>('misc-a')
  const groups = [{ value: 'Misc', items: [{ value: 'misc-a' }] }, { value: 'Web', items: [{ value: 'web-a' }] }]
  const state = scope.run(() => useDisclosureGroups(() => groups, () => selected.value))!
  state.setOpen('Misc', false)
  state.setOpen('Web', false)
  selected.value = 'web-a'
  await nextTick()
  expect(state.isOpen('Web')).toBe(true)
  expect(state.isOpen('Misc')).toBe(false)
  scope.stop()
})
