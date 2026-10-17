import { describe, expect, test } from 'bun:test'
import { minimalTextChange, shouldApplyExternalModel } from '../app/lib/editor-model-sync'

describe('editor model sync', () => {
  test('identical text needs no change', () => {
    expect(minimalTextChange('abc', 'abc')).toBeNull()
  })

  test('an insertion in the middle keeps the surrounding text in place', () => {
    expect(minimalTextChange('abcdef', 'abXcdef')).toEqual({ from: 2, to: 2, insert: 'X' })
    expect(minimalTextChange('abc', 'abcXYZ')).toEqual({ from: 3, to: 3, insert: 'XYZ' })
  })

  test('a deletion only removes the differing range', () => {
    expect(minimalTextChange('abXcdef', 'abcdef')).toEqual({ from: 2, to: 3, insert: '' })
    expect(minimalTextChange('abcXYZ', 'abc')).toEqual({ from: 3, to: 6, insert: '' })
  })

  test('a replacement reports the changed middle only', () => {
    expect(minimalTextChange('hello world', 'hello there')).toEqual({ from: 6, to: 11, insert: 'there' })
  })

  test('fully different documents replace their changed span', () => {
    expect(minimalTextChange('abc', 'xyz')).toEqual({ from: 0, to: 3, insert: 'xyz' })
    expect(minimalTextChange('', 'draft')).toEqual({ from: 0, to: 0, insert: 'draft' })
  })

  test('external model values are ignored when they echo the editor or match the document', () => {
    expect(shouldApplyExternalModel('abc', 'abc', 'abc')).toBeFalse()
    expect(shouldApplyExternalModel('abc', null, 'abc')).toBeFalse()
    expect(shouldApplyExternalModel('abc', undefined, 'abc')).toBeFalse()
  })

  test('a real external change is applied once', () => {
    expect(shouldApplyExternalModel('typed', 'loaded draft', 'typed')).toBeTrue()
  })
})
