import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import {
  applyCtfInteraction,
} from '../../utils/game-config'

/** Replaces only the Runtime portion of the persisted challenge definition. */
export function mergeChallengeRuntimeDefinition(
  persisted: DefinitionModel,
  draft: DefinitionModel,
): DefinitionModel {
  const merged = structuredClone(persisted)
  merged.runtime = structuredClone(draft.runtime)
  return merged
}

/** Replaces mode-owned fields while retaining the last persisted Runtime. */
export function mergeChallengeModeDefinition(
  persistedMode: GameModeValue,
  persistedDefinition: DefinitionModel,
  draftMode: GameModeValue,
  draft: DefinitionModel,
): DefinitionModel {
  if (draftMode !== persistedMode)
    return structuredClone(draft)

  const persisted = structuredClone(persistedDefinition)
  persisted.interactionKind = draft.interactionKind
  persisted.checker = draft.checker
  persisted.checkerJob = draft.checkerJob
  persisted.flagInjection = draft.flagInjection
  persisted.flagTemplate = draft.flagTemplate
  persisted.patchEntrypoint = draft.patchEntrypoint
  persisted.patchCommand = draft.patchCommand
  persisted.patchTimeoutSeconds = draft.patchTimeoutSeconds
  persisted.readyTimeoutSeconds = draft.readyTimeoutSeconds
  persisted.maximumPatchUploadBytes = draft.maximumPatchUploadBytes
  persisted.checkerFixInput = draft.checkerFixInput
  persisted.checkerAllowRoot = draft.checkerAllowRoot
  if (draftMode === 'Ctf')
    applyCtfInteraction(persisted, draft.interactionKind)
  return persisted
}

export function challengeRuntimeDefinitionsEqual(
  _mode: GameModeValue,
  left: DefinitionModel,
  right: DefinitionModel,
): boolean {
  return deepEqual(left.runtime, right.runtime)
}

function deepEqual(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true
  if (Array.isArray(left) && Array.isArray(right))
    return left.length === right.length && left.every((value, index) => deepEqual(value, right[index]))
  if (left === null || right === null || typeof left !== 'object' || typeof right !== 'object')
    return false
  const leftEntries = Object.entries(left as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b))
  const rightEntries = Object.entries(right as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b))
  return leftEntries.length === rightEntries.length
    && leftEntries.every(([key, value], index) =>
      key === rightEntries[index]?.[0] && deepEqual(value, rightEntries[index]?.[1]))
}
