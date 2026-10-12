import { awdPresetConstraints, resolveAwdPresetValues } from '../../utils/awd-config-fields'
import { useAwdRuleEditor } from './useAwdRuleEditor'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import { markRaw, toRefs } from 'vue'

import { RotateCcw } from '@lucide/vue'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../api'
import type { ConfigValues, GameModeValue } from '../../utils/game-config'
import { buildConfigValues, competitionConfigFields, fieldDefaultValue, readConfigValues } from '../../utils/game-config'
import ConfigFieldInputComponent from './ConfigFieldInput.vue'

/** Owns state, effects and commands for CompetitionModeConfigEditor. */
export function useCompetitionModeConfigEditor(props: Readonly<{
  mode: GameModeValue
  competitionStatus?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
  configuration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  readonly: boolean
  loading: boolean
  saving: boolean
}>,
emit: { (event: "save", ...args: [configuration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract, reason?: string]): void }) {
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
    if (awdEditor.updateAwdField(key, value)) return
    values.value = { ...values.value, [key]: value }
  }

  function resetToCurrentDefaults() {
    values.value = Object.fromEntries(
      fields.value.map(field => [field.key, fieldDefaultValue(field)]),
    )
    parseFailed.value = false
  }

  const effectiveValues = computed(() => props.mode === 'Awd' ? resolveAwdPresetValues(values.value) : values.value)
  const awdEditor = useAwdRuleEditor(props, fields, effectiveValues, patch => {
    const baseline = patch.preset === 'Custom' ? awdPresetConstraints(effectiveValues.value) : {}
    values.value = { ...values.value, ...baseline, ...patch }
  })

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
    if (!awdEditor.canSaveRules.value) return
    emit('save', serialized.value, awdEditor.changeReason.value.trim() || undefined)
  }

  const ConfigFieldInput = markRaw(ConfigFieldInputComponent)

  return {
      ...toRefs(props),
      ...awdEditor,
      RotateCcw,
      fields,
      values: effectiveValues,
      parseFailed,
      updateField,
      resetToCurrentDefaults,
      dirty,
      save,
      ConfigFieldInput
    }
}

export type CompetitionModeConfigEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionModeConfigEditor>>>
