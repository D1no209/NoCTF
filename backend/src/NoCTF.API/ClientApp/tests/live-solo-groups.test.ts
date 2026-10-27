import { describe, expect, test } from 'bun:test'
import { groupDraft, groupOffsets, validGroup, moveGroupQuestion, groupValidationError } from '../app/features/live-solo/group-draft'
import { message } from '../app/utils/i18n'
describe('LiveSolo group editing', () => {
  test('an opening at 1200 seconds identifies the second question and its inherited 900-second limit', () => {
    const draft = groupDraft({ name: 'PWN1', questions: [
      { competitionChallengeId: 'rbp', openOffsetSeconds: 0 },
      { competitionChallengeId: 'paper-shell', openOffsetSeconds: 1200 },
    ] })
    expect(groupValidationError(draft, 180, 900)).toEqual({ field: 'offset', index: 1,
      message: message('liveSolo.groups.validation.offsetLimit', { position: 2, offset: 1200, limit: 900 }) })
    draft.limitSeconds = 1800
    expect(groupValidationError(draft, 180, 900)).toBeNull()
    expect(validGroup(draft, 180, 900)).toBe(true)
  })
  test('the last default offset and an opening exactly at the deadline report the actual limit', () => {
    const draft = groupDraft({ name: 'Round', questions: Array.from({ length: 6 }, (_, index) => ({ competitionChallengeId: `question-${index}` })) })
    expect(groupValidationError(draft, 180, 900)).toEqual({ field: 'offset', index: 5,
      message: message('liveSolo.groups.validation.offsetLimit', { position: 6, offset: 900, limit: 900 }) })
    draft.limitSeconds = 901
    expect(validGroup(draft, 180, 900)).toBe(true)
  })
  test('ordered defaults and explicit offsets share the actual group limit', () => {
    const draft = groupDraft({ name: 'Final', questions: [{ competitionChallengeId: 'one' }, { competitionChallengeId: 'two' }] })
    expect(groupOffsets(draft,180)).toEqual([0,180]); expect(validGroup(draft,180,900)).toBe(true)
    draft.limitSeconds = 180; expect(validGroup(draft,180,900)).toBe(false)
    draft.questions[1]!.openOffsetSeconds = 179; expect(validGroup(draft,180,900)).toBe(true)
  })
  test('first opening, repeats and fractional timestamps cannot be silently accepted', () => {
    const draft = groupDraft({ name: 'Round', questions: [{ competitionChallengeId: 'one', openOffsetSeconds: 1 }] })
    expect(validGroup(draft,180,900)).toBe(false); draft.questions[0]!.openOffsetSeconds = 0
    draft.questions.push({ competitionChallengeId: 'one', openOffsetSeconds: 20 }); expect(validGroup(draft,180,900)).toBe(false)
    draft.questions[1]!.competitionChallengeId = 'two'; draft.questions[1]!.openOffsetSeconds = 20.5; expect(validGroup(draft,180,900)).toBe(false)
  })
  test('drafts preserve revision and immutable source while reordering questions', () => {
    const source = { id: 'group', concurrencyStamp: 'stamp', name: 'A', questions: [{ competitionChallengeId: 'a' }, { competitionChallengeId: 'b' }] }
    const draft = groupDraft(source); draft.questions = moveGroupQuestion(draft,1,-1)
    expect(draft.id).toBe('group'); expect(draft.expectedStamp).toBe('stamp'); expect(draft.questions[0]!.competitionChallengeId).toBe('b')
    expect(source.questions[0]!.competitionChallengeId).toBe('a')
  })
})
