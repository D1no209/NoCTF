import { describe, expect, test } from 'bun:test'
import { groupDraft, groupOffsets, validGroup, moveGroupQuestion, copyCandidates } from '../app/features/live-solo/group-draft'
describe('LiveSolo group editing', () => {
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
  test('copy picker excludes deleted, patch and other modes', () => {
    expect(copyCandidates([{ id:'good', mode:'Ctf', interactionKind:'FlagSubmission' }, { id:'patch', mode:'Ctf', interactionKind:'PatchVerification' },
      { id:'solo', mode:'LiveSolo' }, { id:'deleted-solo', mode:'LiveSolo', deletedAt:'now' },
      { id:'awd', mode:'Awd', interactionKind:'FlagSubmission' }, { id:'deleted', mode:'Ctf', interactionKind:'FlagSubmission', deletedAt:'now' }]).map(row=>row.id)).toEqual(['good','solo'])
  })
})
