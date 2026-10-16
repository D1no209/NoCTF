import { expect, test } from 'bun:test'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../app/api'
import {
  definitionContractToModel,
  definitionModelToContract,
  EgressPolicy,
  emptyRuntimeTemplate,
} from '../app/utils/game-config'

function containerRuntime(egressPolicy?: string) {
  return {
    kind: 'Container',
    allocation: 'PerTeam',
    limits: { memoryBytes: 268_435_456, cpuMillicores: 500, pidsLimit: 128 },
    ttlSeconds: 3600,
    operationTimeoutSeconds: 60,
    urlBindings: [],
    flagSource: 'Static',
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
        flagEnvironmentVariableName: null,
        internalPorts: [],
      }],
    },
  }
}

function contractWith(egressPolicy?: string) {
  return {
    mode: 'Ctf',
    ctf: { interactionKind: 'FlagSubmission' },
    awd: null,
    awdp: null,
    koh: null,
    liveSolo: null,
    runtime: containerRuntime(egressPolicy),
    flagTemplate: null,
    checker: null,
    patchEntrypoint: null,
    patchCommand: [],
    patchTimeoutSeconds: null,
    readyTimeoutSeconds: null,
    maximumPatchUploadBytes: null,
    checkerFixInput: false,
  } as unknown as NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
}

test('a saved runtime keeps InternetOnly instead of silently reverting to Isolated', () => {
  const model = definitionContractToModel(contractWith('InternetOnly'), 'Ctf')
  expect(model.runtime?.egressPolicy).toBe(EgressPolicy.InternetOnly)

  const written = definitionModelToContract('Ctf', model)
  expect(written.runtime?.egressPolicy).toBe('InternetOnly')
})

test('a runtime without an egress policy still defaults to Isolated', () => {
  const model = definitionContractToModel(contractWith(), 'Ctf')
  expect(model.runtime?.egressPolicy).toBe(EgressPolicy.Isolated)
  expect(definitionModelToContract('Ctf', model).runtime?.egressPolicy).toBe('Isolated')
})

test('the empty runtime template starts isolated', () => {
  expect(emptyRuntimeTemplate('Ctf').egressPolicy).toBe(EgressPolicy.Isolated)
})
