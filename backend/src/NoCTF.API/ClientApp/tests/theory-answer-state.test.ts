import { describe, expect, test } from 'bun:test'
import { acknowledgeAnswer, acceptRemoteAnswer, createAnswer, overwriteRemoteAnswer, prepareAnswer, reconcileAnswer, sameAnswer, selectAnswer } from '../app/features/theory/answer-state'
import { renderMarkdown } from '../app/lib/markdown'

describe('shared theory answers', () => {
  test('selection saves stable option identities and does not classify correctness', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['option-b', 'option-a'])
    const request = prepareAnswer(draft, 'request-1')!
    expect(request).toEqual({ requestId: 'request-1', revision: 'r1', selected: ['option-b', 'option-a'] })
    expect(draft.dirty).toBe(true)
    expect('correct' in draft).toBe(false)
    expect(sameAnswer(['option-a', 'option-b'], request.selected)).toBe(true)
  })
  test('remote edits retain unsaved local selections and require a decision', () => {
    const draft = createAnswer({ revision: 'r1', selected: ['a'] })
    selectAnswer(draft, ['b'])
    reconcileAnswer(draft, { revision: 'r2', selected: ['c'] })
    expect(draft.selected).toEqual(['b'])
    expect(draft.conflict).toBe(true)
    expect(prepareAnswer(draft, 'must-not-send')).toBeNull()
    overwriteRemoteAnswer(draft)
    expect(prepareAnswer(draft, 'confirmed')).toEqual({ requestId: 'confirmed', revision: 'r2', selected: ['b'] })
  })
  test('accepting a teammate answer clears the unsaved state', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['a']); reconcileAnswer(draft, { revision: 'r2', selected: ['b'] }); acceptRemoteAnswer(draft)
    expect(draft.selected).toEqual(['b'])
    expect(draft.base.revision).toBe('r2')
    expect(draft.dirty).toBe(false)
    expect(draft.conflict).toBe(false)
  })
  test('typing during a save remains dirty after the older request succeeds', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['a']); const request = prepareAnswer(draft, 'save-a')!
    selectAnswer(draft, ['b']); acknowledgeAnswer(draft, request, { revision: 'r2', selected: ['a'] })
    expect(draft.selected).toEqual(['b']); expect(draft.dirty).toBe(true); expect(draft.conflict).toBe(false)
    expect(prepareAnswer(draft, 'save-b')!.revision).toBe('r2')
  })
  test('a transport retry keeps the original request identity and body', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['a']); const pending = prepareAnswer(draft, 'lost-response')!
    draft.saving = false; selectAnswer(draft, ['b'])
    expect(prepareAnswer(draft, 'new-id')).toBe(pending)
    expect(pending.selected).toEqual(['a'])
    expect(pending.requestId).toBe('lost-response')
  })
  test('an authoritative refresh can confirm an accepted save after response loss', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['a']); prepareAnswer(draft, 'lost'); draft.saving = false
    reconcileAnswer(draft, { revision: 'r2', selected: ['a'] })
    expect(draft.pending).toBeNull(); expect(draft.dirty).toBe(false); expect(draft.base.revision).toBe('r2')
  })
  test('a replay returning a newer teammate selection is not mistaken for our saved value', () => {
    const draft = createAnswer({ revision: 'r1', selected: [] })
    selectAnswer(draft, ['a']); const pending = prepareAnswer(draft, 'replay')!
    acknowledgeAnswer(draft, pending, { revision: 'r3', selected: ['b'] })
    expect(draft.selected).toEqual(['a']); expect(draft.conflict).toBe(true)
  })
})

describe('protected theory Markdown', () => {
  test('only explicitly authorized blob images render', () => {
    const html = renderMarkdown('![safe](theory-file:one) ![remote](https://example.invalid/pixel)', undefined,
      { 'theory-file:one': 'blob:http://127.0.0.1/authorized' })
    expect(html).toContain('blob:http://127.0.0.1/authorized')
    expect(html).not.toContain('https://example.invalid')
  })
  test('raw HTML cannot bypass the private image allowlist', () => {
    const html = renderMarkdown('<img src="https://example.invalid/secret"><video src="https://example.invalid/video"></video><script>steal()</script>', undefined, {})
    expect(html).not.toContain('example.invalid'); expect(html).not.toContain('steal'); expect(html).not.toContain('<script')
  })
})
