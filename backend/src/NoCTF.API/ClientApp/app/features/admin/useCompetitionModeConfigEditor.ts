import { markRaw, toRefs } from 'vue'

import { RotateCcw } from '@lucide/vue'
import type { ConfigValues, GameModeValue } from '../../utils/game-config'
import { competitionConfigFields, fieldDefaultValue, parseConfigValues, serializeConfigValues } from '../../utils/game-config'
import ConfigFieldInputComponent from './ConfigFieldInput.vue'

/** Owns state, effects and commands for CompetitionModeConfigEditor. */
export function useCompetitionModeConfigEditor(props: Readonly<Omit<{
  mode: GameModeValue
  /** 服务器端当前配置 JSON。 */
  json?: string | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}, "json" | "readonly" | "loading" | "saving"> & Required<Pick<{
  mode: GameModeValue
  /** 服务器端当前配置 JSON。 */
  json?: string | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}, "json" | "readonly" | "loading" | "saving">>>,
emit: { (event: "save", ...args: [json: string]): void }) {
  const fields = computed(() => competitionConfigFields(props.mode))

  const values = ref<ConfigValues>({})

  const parseFailed = ref(false)

  watch(
    [() => props.json, () => props.mode],
    () => {
      const parsed = parseConfigValues(props.json, fields.value)
      if (parsed) {
        values.value = parsed.values
        parseFailed.value = false
      }
      else {
        values.value = {}
        parseFailed.value = true
      }
    },
    { immediate: true },
  )

  function updateField(key: string, value: unknown) {
    values.value = { ...values.value, [key]: value }
  }

  function resetToCurrentDefaults() {
    values.value = Object.fromEntries(
      fields.value.map(field => [field.key, fieldDefaultValue(field)]),
    )
    parseFailed.value = false
  }

  function canonicalize(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(canonicalize)
    if (value !== null && typeof value === 'object') {
      return Object.fromEntries(
        Object.entries(value as Record<string, unknown>)
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([k, v]) => [k, canonicalize(v)]),
      )
    }
    return value
  }

  function normalize(json: string): string | null {
    try {
      return JSON.stringify(canonicalize(JSON.parse(json)))
    }
    catch {
      return null
    }
  }

  const serialized = computed(() => serializeConfigValues(props.mode, fields.value, values.value, { rules: false }))

  const dirty = computed(() => {
    if (parseFailed.value) return false
    const original = normalize(props.json ?? '')
    if (original === null) return true
    return normalize(serialized.value) !== original
  })

  function save() {
    emit('save', serialized.value)
  }

  const ConfigFieldInput = markRaw(ConfigFieldInputComponent)

  return {
      ...toRefs(props),
      RotateCcw,
      fields,
      values,
      parseFailed,
      updateField,
      resetToCurrentDefaults,
      dirty,
      save,
      ConfigFieldInput
    }
}

export type CompetitionModeConfigEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionModeConfigEditor>>>
