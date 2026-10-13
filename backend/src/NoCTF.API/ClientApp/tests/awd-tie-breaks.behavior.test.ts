import { expect, test } from 'bun:test'
import { reactive } from 'vue'
import { useAwdTieBreakEditor } from '../app/features/admin/awd/useAwdTieBreakEditor'
import { defaultAwdTieBreaks, validAwdTieBreaks, type AwdTieBreak } from '../app/utils/awd-tie-breaks'
import { competitionConfigFields, buildConfigValues, readConfigValues } from '../app/utils/game-config'
import { mockModeConfiguration } from '../mock/data/fixtures'

test('ordered criteria survive reactive edits and typed configuration serialization', () => {
  const props = reactive({ modelValue: defaultAwdTieBreaks(), disabled: false })
  const editor = useAwdTieBreakEditor(props, (_, value) => { props.modelValue = value })
  editor.move(2, -1); editor.move(1, -1)
  editor.updateDirection(0, 'Ascending')
  expect(props.modelValue[0]).toEqual({ field: 'AttackCount', direction: 'Ascending' })
  const fields = competitionConfigFields('Awd'), config = mockModeConfiguration('Awd')
  const loaded = readConfigValues(config!, fields, { rules: false })!
  const values = reactive({ ...loaded.values, awdTieBreakers: props.modelValue })
  const saved = buildConfigValues('Awd', fields, values, { rules: false })
  expect(readConfigValues(saved, fields, { rules: false })?.values.awdTieBreakers).toEqual(props.modelValue)
  expect((saved.awd as { options: { preset: string } }).options.preset).toBe('Fixed')
  expect(Object.keys((saved.awd as { tieBreakers: object[] }).tieBreakers[0]!)).toEqual(['field', 'direction'])
})

test('duplicates, invalid protocol values and removing the last criterion are blocked', () => {
  const props = reactive({ modelValue: defaultAwdTieBreaks(), disabled: false })
  const editor = useAwdTieBreakEditor(props, (_, value) => { props.modelValue = value })
  editor.updateField(0, 'AttackCount'); expect(props.modelValue[0]!.field).toBe('AttackPoints')
  editor.updateDirection(0, 'Sideways'); expect(props.modelValue[0]!.direction).toBe('Descending')
  editor.move(0, -1); expect(props.modelValue[0]!.field).toBe('AttackPoints')
  expect(editor.canAdd.value).toBe(false)
  for (let i = 0; i < 5; i++) editor.remove(0)
  expect(props.modelValue).toHaveLength(1)
  expect(editor.valid.value).toBe(true)
  editor.add(); expect(props.modelValue).toHaveLength(2)
  editor.reset(); expect(props.modelValue).toEqual(defaultAwdTieBreaks())
  expect(validAwdTieBreaks([])).toBe(false)
  expect(validAwdTieBreaks([props.modelValue[0]!, props.modelValue[0]!])).toBe(false)
  expect(validAwdTieBreaks([{ field: 'Unknown', direction: 'Ascending' } as unknown as AwdTieBreak])).toBe(false)
})

test('readonly operations cannot mutate the order or directions', () => {
  const props = reactive({ modelValue: defaultAwdTieBreaks(), disabled: true })
  let writes = 0
  const editor = useAwdTieBreakEditor(props, () => { writes++ })
  editor.move(1, -1); editor.remove(0); editor.add(); editor.reset(); editor.updateDirection(0, 'Ascending')
  expect(writes).toBe(0)
})
