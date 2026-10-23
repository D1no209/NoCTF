import type { NoCtfapiEndpointsChallengesAwdPublicStoreRuleResponse } from '../../api'
import { awdPresetConstraints, resolveAwdPresetValues } from '../../utils/awd-config-fields'
import { useAwdRuleEditor } from './useAwdRuleEditor'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import { markRaw, toRefs } from 'vue'
import type { NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract, NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../api'

import type { ConfigFieldDef, GameModeValue } from '../../utils/game-config'
import { challengeRuleFields, readConfigValues } from '../../utils/game-config'
import { useConfigDraft } from './useConfigDraft'
import ConfigFieldInputComponent from './ConfigFieldInput.vue'

/** Owns state, effects and commands for ChallengeRulesEditor. */
export function useChallengeRulesEditor(props: Readonly<{
  mode: GameModeValue
  scorePreset: import('~/api').NoCtfapiEndpointsAdministrationCompetitionsCompetitionScorePresetResponse | null
  awdStores?: NoCtfapiEndpointsChallengesAwdPublicStoreRuleResponse[]
  competitionStatus?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
  rules: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract | null
  readonly: boolean
  loading: boolean
  saving: boolean
  inheritedConfiguration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null
  hiddenKeys: string[]
}>,
emit: { (event: "save", ...args: [rules: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract, reason?: string]): void }) {
  const fields = computed(() => challengeRuleFields(props.mode)
    .filter(field => !props.hiddenKeys.includes(field.key)))

  const overridden = ref<Record<string, boolean>>({})

  const inheritedValues = computed(() => {
    const values = readConfigValues(
      props.inheritedConfiguration as Record<string, unknown> | null,
      fields.value,
      { rules: false },
    )?.values ?? {}
    if (props.mode === 'Ctf' && props.scorePreset?.scoreCurve) values.scoreCurve = { ...props.scorePreset.scoreCurve }
    return values
  })
  const scoreCurveSource = computed(() => overridden.value.scoreCurve ? translate('scorePresets.source.challenge')
    : props.scorePreset ? translate('scorePresets.source.tag', { name: props.scorePreset.name ?? '' }) : translate('scorePresets.source.competition'))
  const hasScorePreset = computed(() => props.mode === 'Ctf' && !!props.scorePreset)

  const { values, parseFailed, serialized, dirty } = useConfigDraft<NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract>({
    mode: () => props.mode,
    rules: true,
    fields,
    source: () => props.rules,
    buildExtras: () => ({ overridden: overridden.value }),
    onParsed: parsed => { overridden.value = parsed.overridden },
    onParseFailure: () => { overridden.value = {} },
  })

  function updateField(key: string, value: unknown) {
    if (awdEditor.updateAwdField(key, value)) return
    values.value = { ...values.value, [key]: value }
  }

  function setOverride(field: ConfigFieldDef, on: boolean) {
    if (on && !(overridden.value[field.key] ?? false) && inheritedValues.value[field.key] !== undefined) {
      values.value = {
        ...values.value,
        [field.key]: structuredClone(inheritedValues.value[field.key]),
      }
    }
    if (props.mode === 'Awd' && field.key !== 'preset' && field.group && ['scoring', 'sla', 'penalty', 'composition'].includes(field.group)) {
      const baseline = Object.fromEntries(Object.entries(awdPresetConstraints(effectiveValues.value))
        .filter(([key]) => key !== field.key && fields.value.some(field => field.key === key)))
      values.value = { ...values.value, ...baseline, preset: 'Custom' }
      overridden.value = { ...overridden.value, ...Object.fromEntries(Object.keys(baseline).map(key => [key, true])), preset: true }
    }
    overridden.value = { ...overridden.value, [field.key]: on }
  }

  function rawDisplayedValue(field: ConfigFieldDef): unknown {
    return (overridden.value[field.key] ?? false)
      ? values.value[field.key]
      : (inheritedValues.value[field.key] ?? values.value[field.key])
  }

  const effectiveValues = computed(() => {
    const values = Object.fromEntries(fields.value.map(field => [field.key, rawDisplayedValue(field)]))
    return props.mode === 'Awd' ? resolveAwdPresetValues(values) : values
  })
  function displayedValue(field: ConfigFieldDef): unknown { return effectiveValues.value[field.key] }
  const awdEditor = useAwdRuleEditor(props, fields, effectiveValues, patch => {
    const baseline = patch.preset === 'Custom' ? awdPresetConstraints(effectiveValues.value) : {}
    const changes = Object.fromEntries(Object.entries({ ...baseline, ...patch }).filter(([key]) => fields.value.some(field => field.key === key)))
    values.value = { ...values.value, ...changes }
    overridden.value = { ...overridden.value, ...Object.fromEntries(Object.keys(changes).map(key => [key, true])) }
  })

  const storeRules = computed(() => (values.value.awdStoreRules ?? []) as import('../../api').NoCtfapiEndpointsAdministrationChallengesAwdStoreRuleContract[])
  const storeRows = computed(() => {
    const stores = props.awdStores ?? []
    const totalWeight = stores.reduce((sum, store) => sum + (storeRules.value.find(rule => rule.storeId === store.storeId)?.weight ?? 1), 0)
    return stores.map(store => {
      const rule = storeRules.value.find(rule => rule.storeId === store.storeId)
      return { ...store, weightOverride: rule?.weight ?? null, flagsOverride: rule?.flagsPerIssue ?? null,
        effectiveFlags: rule?.flagsPerIssue ?? Number(effectiveValues.value.flagsPerStore ?? 1),
        share: totalWeight > 0 ? ((rule?.weight ?? 1) / totalWeight * 100).toFixed(2) : '—' }
    })
  })
  const storeRulesValid = computed(() => storeRules.value.every(rule => (rule.weight ?? 1) > 0 && (rule.weight ?? 1) <= 100
    && Math.abs((rule.weight ?? 1) * 1e6 - Math.round((rule.weight ?? 1) * 1e6)) < 0.000001
    && (rule.flagsPerIssue == null || (Number.isInteger(rule.flagsPerIssue) && rule.flagsPerIssue >= 1 && rule.flagsPerIssue <= 64))))
  function updateStoreRule(storeId: string | undefined, key: 'weight' | 'flagsPerIssue', value: number | null) {
    if (props.readonly || !storeId) return
    const current = storeRules.value.find(rule => rule.storeId === storeId)
    const updated = { ...current, storeId, [key]: key === 'weight' ? (value ?? 1) : value }
    const remaining = storeRules.value.filter(rule => rule.storeId !== storeId)
    if ((updated.weight ?? 1) !== 1 || updated.flagsPerIssue != null) remaining.push(updated)
    values.value = { ...values.value, awdStoreRules: remaining }
  }
  function presetControlled(field: ConfigFieldDef): boolean { return field.key in awdPresetConstraints(effectiveValues.value) }

  function save() {
    if (!awdEditor.canSaveRules.value || !storeRulesValid.value) return
    emit('save', serialized.value, awdEditor.changeReason.value.trim() || undefined)
  }

  const ConfigFieldInput = markRaw(ConfigFieldInputComponent)

  return {
      ...toRefs(props),
      ...awdEditor,
      scoreCurveSource, hasScorePreset,
      canSaveRules: computed(() => awdEditor.canSaveRules.value && storeRulesValid.value),
      storeRows, storeRulesValid, updateStoreRule, presetControlled,
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
