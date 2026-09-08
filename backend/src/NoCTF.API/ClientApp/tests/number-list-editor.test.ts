import { describe, expect, test } from 'bun:test'
import { updateNullableNumber } from "../app/components/ui/list-editor/number-list"

describe('number list editor', () => {
  test('keeps the row when a numeric input is temporarily empty', () => {
    const values = updateNullableNumber([8080], 0, null)

    expect(values).toEqual([null])
    expect(values).toHaveLength(1)
  })

  test('accepts a corrected number without recreating the row', () => {
    const values = updateNullableNumber([null], 0, 9090)

    expect(values).toEqual([9090])
  })
})
