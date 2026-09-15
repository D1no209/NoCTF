import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import {
  applyCtfInteraction,
  parseDefinition,
  serializeDefinition,
} from '../../utils/game-config'

/** Replaces only the Runtime portion of the persisted challenge definition. */
export function mergeChallengeRuntimeDefinition(
  mode: GameModeValue,
  persistedJson: string,
  draft: DefinitionModel,
): string | null {
  const persisted = parseDefinition(persistedJson, mode)
  if (!persisted) return null
  persisted.runtime = draft.runtime
  return serializeDefinition(mode, persisted)
}

/** Replaces mode-owned fields while retaining the last persisted Runtime. */
export function mergeChallengeModeDefinition(
  persistedMode: GameModeValue,
  persistedJson: string,
  draftMode: GameModeValue,
  draft: DefinitionModel,
): string | null {
  if (draftMode !== persistedMode)
    return serializeDefinition(draftMode, draft)

  const persisted = parseDefinition(persistedJson, persistedMode)
  if (!persisted) return null
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
  return serializeDefinition(draftMode, persisted)
}

export function challengeRuntimeDefinitionsEqual(
  mode: GameModeValue,
  left: DefinitionModel,
  right: DefinitionModel,
): boolean {
  const leftJson = JSON.parse(serializeDefinition(mode, left)) as { runtime?: unknown }
  const rightJson = JSON.parse(serializeDefinition(mode, right)) as { runtime?: unknown }
  return JSON.stringify(leftJson.runtime ?? null) === JSON.stringify(rightJson.runtime ?? null)
}
