import { expect, test } from 'bun:test'
import {
  emptyDefinition, emptyRuntimeTemplate, emptyRuntimeService,
  renameRuntimeService, runtimeServiceIsReferenced,
} from '../app/utils/game-config'

test('renaming a service updates every entry and operation reference together', () => {
  const model = emptyDefinition('Awd')
  model.runtime = emptyRuntimeTemplate('Awd')
  model.checker = { job: { image: 'checker', command: [], environment: {}, timeoutSeconds: 30 }, targetServiceName: 'main' }
  model.flagInjection = { command: 'set-flag ${FLAG}', timeoutSeconds: 30, serviceName: 'main' }
  model.runtime.controlCheckUrlBinding = { serviceName: 'main', containerPort: 8080, urlTemplate: 'http://{HOST}:{PORT}/control', exposure: 0 }
  renameRuntimeService(model, 'main', 'web')
  expect(model.runtime.definition.kind).toBe('container')
  if (model.runtime.definition.kind !== 'container') throw new Error('Expected named services')
  expect(model.runtime.definition.services[0]!.name).toBe('web')
  expect(model.runtime.urlBindings.every(entry => entry.serviceName === 'web')).toBeTrue()
  expect(model.runtime.controlCheckUrlBinding.serviceName).toBe('web')
  expect(model.checker.targetServiceName).toBe('web')
  expect(model.flagInjection.serviceName).toBe('web')
  expect(runtimeServiceIsReferenced(model, 'main')).toBeFalse()
  expect(runtimeServiceIsReferenced(model, 'web')).toBeTrue()
})

test('unreferenced auxiliary services can be removed without changing the entry list', () => {
  const model = emptyDefinition('Ctf')
  model.runtime = emptyRuntimeTemplate('Ctf')
  if (model.runtime.definition.kind !== 'container') throw new Error('Expected named services')
  model.runtime.definition.services.push(emptyRuntimeService('db'))
  expect(runtimeServiceIsReferenced(model, 'main')).toBeTrue()
  expect(runtimeServiceIsReferenced(model, 'db')).toBeFalse()
  expect(model.runtime.urlBindings).toHaveLength(1)
})
