import AwdTieBreakEditorComponent from './awd/AwdTieBreakEditor.vue'
import { defaultAwdTieBreaks, validAwdTieBreaks, type AwdTieBreak } from '../../utils/awd-tie-breaks'
import { awdPresetConstraints, resolveAwdPresetValues } from '../../utils/awd-config-fields'
import { useAwdRuleEditor } from './useAwdRuleEditor'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import { markRaw, toRefs } from 'vue'

import { RotateCcw } from '@lucide/vue'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../api'
import type { GameModeValue } from '../../utils/game-config'
import { competitionConfigFields, fieldDefaultValue } from '../../utils/game-config'
import { useConfigDraft } from './useConfigDraft'
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

  const { values, parseFailed, serialized, dirty } = useConfigDraft<NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract>({
    mode: () => props.mode,
    rules: false,
    fields,
    source: () => props.configuration,
  })

  function updateField(key: string, value: unknown) {
    if (awdEditor.updateAwdField(key, value)) return
    values.value = { ...values.value, [key]: value }
  }

  function resetToCurrentDefaults() {
    values.value = Object.fromEntries(
      fields.value.map(field => [field.key, fieldDefaultValue(field)]),
    )
    if (props.mode === 'Awd') values.value.awdTieBreakers = defaultAwdTieBreaks()
    parseFailed.value = false
  }

  const effectiveValues = computed(() => props.mode === 'Awd' ? resolveAwdPresetValues(values.value) : values.value)
  const awdEditor = useAwdRuleEditor(props, fields, effectiveValues, patch => {
    const baseline = patch.preset === 'Custom' ? awdPresetConstraints(effectiveValues.value) : {}
    values.value = { ...values.value, ...baseline, ...patch }
  })

  const tieBreakers = computed(() => (values.value.awdTieBreakers ?? defaultAwdTieBreaks()) as AwdTieBreak[])
  const canSaveRules = computed(() => awdEditor.canSaveRules.value && (props.mode !== 'Awd' || validAwdTieBreaks(tieBreakers.value)))
  function updateTieBreakers(value: AwdTieBreak[]) { if (!props.readonly && !props.saving) values.value = { ...values.value, awdTieBreakers: value.map(rule => ({ ...rule })) } }
  function save() {
    if (!canSaveRules.value) return
    emit('save', serialized.value, awdEditor.changeReason.value.trim() || undefined)
  }

  const ConfigFieldInput = markRaw(ConfigFieldInputComponent)

  return {
      ...toRefs(props),
      ...awdEditor,
      canSaveRules, tieBreakers, updateTieBreakers, AwdTieBreakEditor: markRaw(AwdTieBreakEditorComponent),
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
