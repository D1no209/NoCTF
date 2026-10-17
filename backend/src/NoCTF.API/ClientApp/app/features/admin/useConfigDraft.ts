import type { ComputedRef } from 'vue'
import type { ConfigFieldDef, ConfigValues, GameModeValue } from '../../utils/game-config'
import { buildConfigValues, readConfigValues } from '../../utils/game-config'

export interface ConfigDraftOptions<TContract> {
  /** Current editor mode; LiveSolo keeps an empty field catalog. */
  mode: () => GameModeValue
  /** `true` when the draft edits the challenge rules overlay instead of the mode branch. */
  rules: boolean
  fields: ComputedRef<ConfigFieldDef[]>
  /** Persisted contract currently loaded into the editor. */
  source: () => TContract | null | undefined
  /** Extra build options, e.g. the rules overlay's per-field override map. */
  buildExtras?: () => { overridden?: Record<string, boolean> }
  /** Rehydrate extra draft state from a successful parse. */
  onParsed?: (parsed: { values: ConfigValues, overridden: Record<string, boolean> }) => void
  /** Clear extra draft state when the persisted contract cannot be parsed. */
  onParseFailure?: () => void
}

export function canonicalizeConfigValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonicalizeConfigValue)
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, entry]) => entry !== null && entry !== undefined)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, entry]) => [key, canonicalizeConfigValue(entry)]),
    )
  }
  return value
}

/** Property-order-insensitive form used to decide whether a draft is dirty. */
export function normalizeConfigValue(value: unknown): string {
  return JSON.stringify(canonicalizeConfigValue(value))
}

/**
 * Shared draft state for the challenge rules and competition mode editors:
 * parse the persisted contract into editable values, rebuild it on demand and
 * report property-order-insensitive dirtiness. Field-level rules stay in the
 * owning feature controller.
 */
export function useConfigDraft<TContract>(options: ConfigDraftOptions<TContract>) {
  const values = ref<ConfigValues>({})
  const parseFailed = ref(false)

  watch(
    [options.source, options.mode],
    () => {
      const parsed = readConfigValues(
        options.source() as unknown as Record<string, unknown> | null,
        options.fields.value,
        { rules: options.rules },
      )
      if (parsed) {
        values.value = parsed.values
        parseFailed.value = false
        options.onParsed?.(parsed)
      }
      else {
        values.value = {}
        parseFailed.value = true
        options.onParseFailure?.()
      }
    },
    { immediate: true },
  )

  const serialized = computed(() => buildConfigValues(
    options.mode(),
    options.fields.value,
    values.value,
    { rules: options.rules, ...options.buildExtras?.() },
  ) as unknown as TContract)

  const dirty = computed(() =>
    !parseFailed.value
    && normalizeConfigValue(serialized.value) !== normalizeConfigValue(options.source()))

  return { values, parseFailed, serialized, dirty }
}
