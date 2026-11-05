import { expect, test } from 'bun:test'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../app/api'
import {
  definitionContractToModel,
  definitionModelToContract,
  emptyRuntimeTemplate,
} from '../app/utils/game-config'

function containerRuntime(egressPolicy?: string) {
  return {
    kind: 'Container',
    limits: { memoryBytes: 268_435_456, cpuMillicores: 500, pidsLimit: 128 },
    operationTimeoutSeconds: 60,
    urlBindings: [],
    ...(egressPolicy ? { egressPolicy } : {}),
    container: {
      services: [{
        name: 'main',
        image: 'image:latest',
        cpuCores: 0.5,
        memoryMiB: 512,
        command: [],
        arguments: [],
        environment: {},
        internalPorts: [],
      }],
    },
  }
}

function contractWith(egressPolicy?: string) {
  return {
    capabilities: [{ kind: 'Break', break: {} }],
    runtime: containerRuntime(egressPolicy),
  } as unknown as NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
}

test('a saved runtime keeps InternetOnly instead of silently reverting to Isolated', () => {
  const model = definitionContractToModel(contractWith('InternetOnly'))
  expect(model.runtime?.egressPolicy).toBe('InternetOnly')

  const written = definitionModelToContract(model)
  expect(written.runtime?.egressPolicy).toBe('InternetOnly')
})

test('a runtime without an egress policy still defaults to Isolated', () => {
  const model = definitionContractToModel(contractWith())
  expect(model.runtime?.egressPolicy).toBe('Isolated')
  expect(definitionModelToContract(model).runtime?.egressPolicy).toBe('Isolated')
})

test('the empty runtime template starts isolated', () => {
  expect(emptyRuntimeTemplate().egressPolicy).toBe('Isolated')
})
