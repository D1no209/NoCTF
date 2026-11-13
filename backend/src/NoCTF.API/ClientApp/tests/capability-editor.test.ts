import { describe, expect, test } from 'bun:test'
import { challengeTemplateDraftContent, enabledCapabilitySections } from '../app/features/admin/capability-editor'
import { emptyDefinition, setChallengeCapability, definitionModelToContract, definitionContractToModel } from '../app/utils/game-config'
import { validateChallengeTemplateIssues } from '../app/lib/challenge-template-validation'
import { challengeTemplateWriteErrorSection } from '../app/lib/challenge-template-error'

describe('capability editor draft', () => {
  test('saving basic information does not require execution configuration', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Check', true)
    setChallengeCapability(model, 'Control', true)
    const draft = { title: 'Draft', direction: 'Web', definition: model }
    expect(validateChallengeTemplateIssues(draft, false)).toEqual([])
    expect(validateChallengeTemplateIssues(draft).length).toBeGreaterThan(0)
    expect(validateChallengeTemplateIssues({ ...draft, title: '' }, false)[0]?.section).toBe('basic')
  })
  test('server validation fields identify the matching configuration page', () => {
    const model = emptyDefinition(); setChallengeCapability(model, 'Control', true)
    expect(challengeTemplateWriteErrorSection({ errors: { 'Content.Title': ['invalid'] } }, model)).toBe('basic')
    expect(challengeTemplateWriteErrorSection({ errors: { 'Content.Definition.Runtime.Container.Services[0].Image': ['invalid'] } }, model)).toBe('runtime')
    expect(challengeTemplateWriteErrorSection({ errors: { 'Content.Definition.Capabilities[1].Control.Image': ['invalid'] } }, model)).toBe('control')
    expect(challengeTemplateWriteErrorSection({ errors: { 'Content.Definition.Capabilities[0].Break.FlagTemplate': ['invalid'] } }, model)).toBe('flags')
    expect(challengeTemplateWriteErrorSection({ code: 'ReferencedCapabilityConflict' }, model)).toBeNull()
  })

  test('each selected capability contributes its own configuration page', () => {
    const model = emptyDefinition()
    expect(enabledCapabilitySections(model).map(item => item.section)).toEqual(['flags'])
    for (const kind of ['Fix', 'Check', 'Control', 'Questionnaire'] as const) setChallengeCapability(model, kind, true)
    expect(enabledCapabilitySections(model).map(item => item.section)).toEqual(['flags', 'fix', 'check', 'control', 'questionnaire'])
    setChallengeCapability(model, 'Break', false)
    expect(enabledCapabilitySections(model).some(item => item.section === 'flags')).toBeFalse()
  })

  test('turning a capability off and on retains its unsaved container settings', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Control', true)
    model.control!.job.image = 'my-control:latest'
    model.control!.job.environment = { CONTROL_PORT: '8080' }
    setChallengeCapability(model, 'Control', false)
    expect(definitionModelToContract(model).capabilities.some(item => item.kind === 'Control')).toBeFalse()
    setChallengeCapability(model, 'Control', true)
    expect(model.control!.job.image).toBe('my-control:latest')
    expect(model.control!.job.environment.CONTROL_PORT).toBe('8080')
  })

  test('one save includes metadata, runtime and all enabled capabilities', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Check', true)
    model.runtime!.definition.kind === 'container' && (model.runtime!.definition.services[0]!.image = 'target:latest')
    model.checker!.job.image = 'checker:latest'
    const content = challengeTemplateDraftContent({ title: ' Question ', direction: 'Web', description: ' Content ', visibility: 'Private' }, model)
    expect(content.title).toBe('Question')
    expect(content.description).toBe('Content')
    expect(content.definition.runtime?.container?.services[0]?.image).toBe('target:latest')
    expect(content.definition.capabilities.map(item => item.kind)).toEqual(['Break', 'Check'])
    const loaded = definitionContractToModel(content.definition)
    expect(loaded.checker!.job.image).toBe('checker:latest')
  })

  test('empty capability selection is a basic information error', () => {
    const model = emptyDefinition(); setChallengeCapability(model, 'Break', false)
    expect(validateChallengeTemplateIssues({ title: 'Question', direction: 'Web', definition: model })[0]?.section).toBe('basic')
  })

  test('an old Control definition without an image remains visible as requiring configuration', () => {
    const model = emptyDefinition(); setChallengeCapability(model, 'Control', true)
    expect(enabledCapabilitySections(model).find(item => item.kind === 'Control')?.configurationRequired).toBeTrue()
    expect(validateChallengeTemplateIssues({ title: 'Question', direction: 'Web', definition: model })[0]?.section).toBe('control')
    model.control!.job.image = 'control:latest'
    expect(enabledCapabilitySections(model).find(item => item.kind === 'Control')?.configurationRequired).toBeFalse()
  })
})
