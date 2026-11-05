import type { DefinitionModel } from '../../utils/game-config'

function cloneDefinition(value: DefinitionModel): DefinitionModel {
  // DefinitionModel 是 JSON 数据；序列化可读取任意层级的响应式代理并保留独立副本。
  return JSON.parse(JSON.stringify(value)) as DefinitionModel
}

/** Replaces only the Runtime portion of the persisted challenge definition. */
export function mergeChallengeRuntimeDefinition(
  persisted: DefinitionModel,
  draft: DefinitionModel,
): DefinitionModel {
  const merged = cloneDefinition(persisted)
  merged.runtime = cloneDefinition(draft).runtime
  return merged
}

/** Replaces capability fields while retaining the last persisted Runtime. */
export function mergeChallengeCapabilityDefinition(
  persistedDefinition: DefinitionModel,
  draft: DefinitionModel,
): DefinitionModel {
  const savedDraft = cloneDefinition(draft)
  savedDraft.runtime = cloneDefinition(persistedDefinition).runtime
  return savedDraft
}

export function challengeRuntimeDefinitionsEqual(
  left: DefinitionModel,
  right: DefinitionModel,
): boolean {
  return deepEqual(left.runtime, right.runtime)
}

export function challengeCapabilityDefinitionsEqual(left: DefinitionModel, right: DefinitionModel): boolean {
  return deepEqual({ ...left, runtime: null }, { ...right, runtime: null })
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
