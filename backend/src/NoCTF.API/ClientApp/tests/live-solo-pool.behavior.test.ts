import { describe, expect, test } from 'bun:test'
import { deploymentCandidates, poolDraft } from '../app/features/live-solo/pool-draft'
import { keepGroupDraft, takeGroupDraft } from '../app/features/live-solo/group-handoff'
import { groupDraft } from '../app/features/live-solo/group-draft'

describe('LiveSolo material workflows', () => {
  test('competition deployment accepts Break or Fix and Check capabilities across the bank', () => {
    expect(deploymentCandidates([{ id: 'break', capabilities: ['Break'] }, { id: 'repair', capabilities: ['Fix', 'Check'] },
      { id: 'deleted', capabilities: ['Break'], deletedAt: 'now' }, { id: 'fix-only', capabilities: ['Fix'] },
      { id: 'questionnaire', capabilities: ['Questionnaire'] }]).map(x => x.id)).toEqual(['break', 'repair'])
  })
  test('editing a statement keeps the loaded configuration and protected material in independent drafts', () => {
    const source = { title: 'Example', description: 'Statement', flags: [{ flag: 'synthetic', matchKind: 'Exact' as const }],
      definition: { capabilities: [{ kind: 'Break' as const, break: { environmentTargets: [{ serviceName: 'web', variableName: 'FLAG' }] } }] } }
    const draft = poolDraft(source); draft.description = 'Changed'; draft.flags![0]!.flag = 'changed'
    expect(source.description).toBe('Statement'); expect(source.flags[0]!.flag).toBe('synthetic')
    draft.definition.capabilities![0]!.break!.environmentTargets![0]!.variableName = 'OTHER_FLAG'
    expect(source.definition.capabilities[0]!.break.environmentTargets[0]!.variableName).toBe('FLAG')
    expect(draft.definition.capabilities?.[0]?.kind).toBe('Break'); expect(draft.removedAttachmentIds).toEqual([])
  })
  test('attachment-managed flags are preserved instead of silently replaced', () => {
    expect(poolDraft({ flags: [{ specificationKind: 'Attachment' }] }).flags).toBeNull()
  })
  test('pool handoff preserves dirty ordering and concurrency, and is isolated by identity and competition', () => {
    const draft = groupDraft({ id: 'list', concurrencyStamp: 'revision', name: 'List', questions: [{ competitionChallengeId: 'one', openOffsetSeconds: 0 }] })
    keepGroupDraft('user:competition:list', draft, 'original')
    draft.questions[0]!.competitionChallengeId = 'mutated'
    expect(takeGroupDraft('other:competition:list')).toBeUndefined()
    const recovered = takeGroupDraft('user:competition:list')!
    expect(recovered.original).toBe('original'); expect(recovered.draft.expectedStamp).toBe('revision')
    expect(recovered.draft.questions[0]!.competitionChallengeId).toBe('one')
    expect(takeGroupDraft('user:competition:list')).toBeUndefined()
  })
})
