import { markRaw, toRefs } from 'vue'

import { RotateCcw } from '@lucide/vue'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../api'
import type { ConfigValues, GameModeValue } from '../../utils/game-config'
import { buildConfigValues, competitionConfigFields, fieldDefaultValue, readConfigValues } from '../../utils/game-config'
import ConfigFieldInputComponent from './ConfigFieldInput.vue'

/** Owns state, effects and commands for CompetitionModeConfigEditor. */
export function useCompetitionModeConfigEditor(props: Readonly<Omit<{
  mode: GameModeValue
  configuration?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}, "configuration" | "readonly" | "loading" | "saving"> & Required<Pick<{
  mode: GameModeValue
  configuration?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}, "configuration" | "readonly" | "loading" | "saving">>>,
emit: { (event: "save", ...args: [configuration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract]): void }) {
  const fields = computed(() => competitionConfigFields(props.mode))

  const values = ref<ConfigValues>({})

  const parseFailed = ref(false)

  watch(
    [() => props.configuration, () => props.mode],
    () => {
      const parsed = readConfigValues(
        props.configuration as Record<string, unknown> | null,
        fields.value,
        { rules: false },
      )
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
          .filter(([, entry]) => entry !== null && entry !== undefined)
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([k, v]) => [k, canonicalize(v)]),
      )
    }
    return value
  }

  function normalize(value: unknown): string {
    return JSON.stringify(canonicalize(value))
  }

  const serialized = computed(() => buildConfigValues(
    props.mode,
    fields.value,
    values.value,
    { rules: false },
  ) as NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract)

  const dirty = computed(() => {
    if (parseFailed.value) return false
    const original = normalize(props.configuration)
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
