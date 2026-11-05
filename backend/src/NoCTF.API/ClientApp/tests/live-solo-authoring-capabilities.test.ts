import { describe, expect, test } from 'bun:test'
import { gameModeOptions, isGameMode } from '../app/utils/game-modes'
import { defaultDefinition, definitionModelToContract, emptyDefinition, emptyRuntimeTemplate, setChallengeCapability } from '../app/utils/game-config'
import { supportsCompetitionCapabilities } from '../app/features/admin/challenge-competition-placements'
import { deploymentCandidates, poolDraft } from '../app/features/live-solo/pool-draft'
import { validateChallengeTemplateDraft } from '../app/lib/challenge-template-validation'

describe('capability-based LiveSolo authoring', () => {
  test('competition modes retain their bounded protocol and the bank definition carries capability choices', () => {
    expect(gameModeOptions.map(item => item.value)).toEqual(['Ctf', 'Awd', 'Awdp', 'Koh', 'LiveSolo'])
    for (const value of ['livesolo', 'toString', 4, null]) expect(isGameMode(value)).toBeFalse()
    const definition = defaultDefinition()
    expect(definition.capabilities?.map(item => item.kind)).toEqual(['Break'])
    expect('mode' in definition).toBeFalse()
  })

  test('LiveSolo deployment accepts Break or the complete repair chain and excludes Questionnaire', () => {
    expect(supportsCompetitionCapabilities('LiveSolo', ['Break'])).toBeTrue()
    expect(supportsCompetitionCapabilities('LiveSolo', ['Fix', 'Check'])).toBeTrue()
    for (const capabilities of [[], ['Fix'], ['Check'], ['Questionnaire']] as const)
      expect(supportsCompetitionCapabilities('LiveSolo', capabilities)).toBeFalse()
    expect(deploymentCandidates([
      { id: 'break', capabilities: ['Break'] }, { id: 'repair', capabilities: ['Fix', 'Check'] },
      { id: 'survey', capabilities: ['Questionnaire'] }, { id: 'removed', capabilities: ['Break'], deletedAt: '2026-11-01T00:00:00Z' },
    ]).map(item => item.id)).toEqual(['break', 'repair'])
  })

  test('a pool draft keeps the competition completion rule separate and copies capability configuration', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Fix', true); setChallengeCapability(model, 'Check', true)
    const definition = definitionModelToContract(model)
    const draft = poolDraft({ id: 'repair', title: 'Repair', direction: 'Web', completionKind: 'FixAndCheck', definition })
    expect(draft.completionKind).toBe('FixAndCheck')
    expect('completionKind' in draft.definition).toBeFalse()
    expect(draft.definition).toEqual(definition)
    draft.definition.capabilities![1]!.fix!.entrypoint = 'updated.sh'
    expect(definition.capabilities![1]!.fix!.entrypoint).toBe('fix.sh')
  })

  test('a valid standalone Fix capability remains saveable before a consumer chooses its repair chain', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Break', false); setChallengeCapability(model, 'Fix', true)
    model.runtime = emptyRuntimeTemplate()
    model.runtime.urlBindings = []
    if (model.runtime.definition.kind !== 'container') throw new Error('Expected a Container definition')
    model.runtime.definition.services[0]!.image = 'registry.example/challenge:1'
    expect(validateChallengeTemplateDraft({ title: 'Repairable', direction: 'Web', definition: model })).toEqual([])
    expect(supportsCompetitionCapabilities('LiveSolo', model.capabilities)).toBeFalse()
  })
})
