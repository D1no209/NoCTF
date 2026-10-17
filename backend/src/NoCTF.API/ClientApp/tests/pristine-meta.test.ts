import { describe, expect, test } from 'bun:test'
import { shouldHydrateMetaField } from '../app/lib/pristine-meta'

type Meta = { title: string; description: string; capacity: number }

const previous: Meta = { title: 'Old title', description: 'Old description', capacity: 10 }

describe('pristine meta hydration', () => {
  test('untouched fields follow the refreshed resource', () => {
    const current: Meta = { ...previous }
    expect(shouldHydrateMetaField(previous, current, 'title')).toBeTrue()
    expect(shouldHydrateMetaField(previous, current, 'description')).toBeTrue()
  })

  test('fields edited by the user keep their local value', () => {
    const current: Meta = { ...previous, description: 'Typing in the editor', capacity: 25 }
    expect(shouldHydrateMetaField(previous, current, 'title')).toBeTrue()
    expect(shouldHydrateMetaField(previous, current, 'description')).toBeFalse()
    expect(shouldHydrateMetaField(previous, current, 'capacity')).toBeFalse()
  })

  test('the first hydration and forced hydration write every field', () => {
    const current: Meta = { title: 'a', description: 'b', capacity: 1 }
    expect(shouldHydrateMetaField(null, current, 'description')).toBeTrue()
    expect(shouldHydrateMetaField(previous, current, 'description', true)).toBeTrue()
  })
})
