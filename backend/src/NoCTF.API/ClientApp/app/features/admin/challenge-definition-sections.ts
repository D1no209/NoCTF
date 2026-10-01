import { toRaw } from 'vue'
import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import {
  applyCtfInteraction,
} from '../../utils/game-config'

/** Replaces only the Runtime portion of the persisted challenge definition. */
export function mergeChallengeRuntimeDefinition(
  persisted: DefinitionModel,
  draft: DefinitionModel,
): DefinitionModel {
  const merged = structuredClone(toRaw(persisted))
  merged.runtime = structuredClone(toRaw(draft.runtime))
  return merged
}

/** Replaces mode-owned fields while retaining the last persisted Runtime. */
export function mergeChallengeModeDefinition(
  persistedMode: GameModeValue,
  persistedDefinition: DefinitionModel,
  draftMode: GameModeValue,
  draft: DefinitionModel,
): DefinitionModel {
  const savedDraft = structuredClone(toRaw(draft))
  if (draftMode !== persistedMode)
    return savedDraft

  const persisted = structuredClone(toRaw(persistedDefinition))
  persisted.interactionKind = savedDraft.interactionKind
  persisted.checker = savedDraft.checker
  persisted.checkerJob = savedDraft.checkerJob
  persisted.flagInjection = savedDraft.flagInjection
  persisted.flagTemplate = savedDraft.flagTemplate
  persisted.patchEntrypoint = savedDraft.patchEntrypoint
  persisted.patchCommand = savedDraft.patchCommand
  persisted.patchTimeoutSeconds = savedDraft.patchTimeoutSeconds
  persisted.readyTimeoutSeconds = savedDraft.readyTimeoutSeconds
  persisted.maximumPatchUploadBytes = savedDraft.maximumPatchUploadBytes
  persisted.checkerFixInput = savedDraft.checkerFixInput
  if (draftMode === 'Ctf')
    applyCtfInteraction(persisted, savedDraft.interactionKind)
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
