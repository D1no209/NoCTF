import { markRaw, toRefs } from 'vue'
import type { NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract, NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../api'

import type { ConfigFieldDef, ConfigValues, GameModeValue } from '../../utils/game-config'
import { buildConfigValues, challengeRuleFields, readConfigValues } from '../../utils/game-config'
import ConfigFieldInputComponent from './ConfigFieldInput.vue'

/** Owns state, effects and commands for ChallengeRulesEditor. */
export function useChallengeRulesEditor(props: Readonly<Omit<{
  mode: GameModeValue
  rules?: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract | null
  inheritedConfiguration?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
  hiddenKeys?: string[]
}, "rules" | "inheritedConfiguration" | "readonly" | "loading" | "saving" | "hiddenKeys"> & Required<Pick<{
  mode: GameModeValue
  rules?: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract | null
  inheritedConfiguration?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly?: boolean
  loading?: boolean
  saving?: boolean
  hiddenKeys?: string[]
}, "rules" | "inheritedConfiguration" | "readonly" | "loading" | "saving" | "hiddenKeys">>>,
emit: { (event: "save", ...args: [rules: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract]): void }) {
  const fields = computed(() => challengeRuleFields(props.mode)
    .filter(field => !props.hiddenKeys.includes(field.key)))

  const values = ref<ConfigValues>({})

  const overridden = ref<Record<string, boolean>>({})

  const parseFailed = ref(false)

  const inheritedValues = computed(() =>
    readConfigValues(
      props.inheritedConfiguration as Record<string, unknown> | null,
      fields.value,
      { rules: false },
    )?.values ?? {},
  )

  watch(
    [() => props.rules, () => props.mode],
    () => {
      const parsed = readConfigValues(
        props.rules as Record<string, unknown> | null,
        fields.value,
        { rules: true },
      )
      if (parsed) {
        values.value = parsed.values
        overridden.value = parsed.overridden
        parseFailed.value = false
      }
      else {
        values.value = {}
        overridden.value = {}
        parseFailed.value = true
      }
    },
    { immediate: true },
  )

  function updateField(key: string, value: unknown) {
    values.value = { ...values.value, [key]: value }
  }

  function setOverride(field: ConfigFieldDef, on: boolean) {
    if (on && !(overridden.value[field.key] ?? false) && inheritedValues.value[field.key] !== undefined) {
      values.value = {
        ...values.value,
        [field.key]: structuredClone(inheritedValues.value[field.key]),
      }
    }
    overridden.value = { ...overridden.value, [field.key]: on }
  }

  function displayedValue(field: ConfigFieldDef): unknown {
    return (overridden.value[field.key] ?? false)
      ? values.value[field.key]
      : (inheritedValues.value[field.key] ?? values.value[field.key])
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

  const serialized = computed(() =>
    buildConfigValues(props.mode, fields.value, values.value, {
      rules: true,
      overridden: overridden.value,
    }) as NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract,
  )

  const dirty = computed(() => {
    if (parseFailed.value) return false
    const original = normalize(props.rules)
    return normalize(serialized.value) !== original
  })

  function save() {
    emit('save', serialized.value)
  }

  const ConfigFieldInput = markRaw(ConfigFieldInputComponent)

  return {
      ...toRefs(props),
      fields,
      overridden,
      parseFailed,
      updateField,
      setOverride,
      displayedValue,
      dirty,
      save,
      ConfigFieldInput
    }
}

export type ChallengeRulesEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useChallengeRulesEditor>>>
