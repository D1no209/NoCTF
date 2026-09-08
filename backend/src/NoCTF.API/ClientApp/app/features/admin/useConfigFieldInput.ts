import { markRaw, toRefs } from 'vue'

import type { BloodRewardValue, ConfigFieldDef, FlagTemplateModel, PointsCurveValue } from '../../utils/game-config'
import { BLOOD_REWARD_POLICIES, BloodRewardPolicy, emptyFlagTemplate, SCORE_DECAY_MODES, ScoreDecayMode } from '../../utils/game-config'
import PointsDecayCurveComponent from './PointsDecayCurve.vue'

type Events = { 'update:modelValue': [value: unknown] }

/** Owns state, effects and commands for ConfigFieldInput. */
export function useConfigFieldInput(props: Readonly<Omit<{
  field: ConfigFieldDef
  modelValue: unknown
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  field: ConfigFieldDef
  modelValue: unknown
  disabled?: boolean
}, "disabled">>>,
emit: { (event: "update:modelValue", ...args: [value: unknown]): void }) {
  function emitValue(value: unknown) {
    emit('update:modelValue', value)
  }

  function parseNullableNumber(value: unknown): number | null {
    if (value === '' || value === null || value === undefined) return null
    const n = Number(value)
    return Number.isFinite(n) ? n : null
  }

  const numberText = computed(() => typeof props.modelValue === 'number' ? String(props.modelValue) : '')

  const textValue = computed(() => typeof props.modelValue === 'string' ? props.modelValue : '')

  const boolValue = computed(() => props.modelValue === true)

  const selectValue = computed(() => {
    const v = props.modelValue
    return typeof v === 'string' || typeof v === 'number' ? v : undefined
  })

  const curve = computed<PointsCurveValue>(() => {
    const v = props.modelValue as Partial<PointsCurveValue> | undefined
    return {
      initialPoints: v?.initialPoints ?? null,
      minimumPoints: v?.minimumPoints ?? null,
      decayTeamCount: v?.decayTeamCount ?? null,
      decayMode: v?.decayMode ?? ScoreDecayMode.Quadratic,
      customExpression: v?.customExpression ?? null,
    }
  })

  function updateCurve(part: Partial<PointsCurveValue>) {
    emitValue({ ...curve.value, ...part })
  }

  function updateDecayMode(value: unknown) {
    const decayMode = parseNullableNumber(value) ?? curve.value.decayMode
    updateCurve({
      decayMode,
      customExpression: decayMode === ScoreDecayMode.Custom ? curve.value.customExpression : null,
    })
  }

  const rewards = computed<BloodRewardValue[]>(() => Array.isArray(props.modelValue) ? props.modelValue : [])

  function addReward() {
    emitValue([...rewards.value, { policy: BloodRewardPolicy.FixedPoints, value: null }])
  }

  function updateReward(index: number, part: Partial<BloodRewardValue>) {
    emitValue(rewards.value.map((reward, i) => i === index ? { ...reward, ...part } : reward))
  }

  function removeReward(index: number) {
    emitValue(rewards.value.filter((_, i) => i !== index))
  }

  const flagTemplateValue = computed<FlagTemplateModel>(() => {
    const v = props.modelValue as Partial<FlagTemplateModel> | undefined
    const fallback = emptyFlagTemplate()
    return {
      header: v?.header ?? fallback.header,
      bodyTemplate: v?.bodyTemplate ?? fallback.bodyTemplate,
      leetLiteralText: v?.leetLiteralText ?? fallback.leetLiteralText,
    }
  })

  function updateFlagTemplate(part: Partial<FlagTemplateModel>) {
    emitValue({ ...flagTemplateValue.value, ...part })
  }

  const PointsDecayCurve = markRaw(PointsDecayCurveComponent)

  return {
      ...toRefs(props),
      BLOOD_REWARD_POLICIES,
      SCORE_DECAY_MODES,
      ScoreDecayMode,
      emitValue,
      parseNullableNumber,
      numberText,
      textValue,
      boolValue,
      selectValue,
      curve,
      updateCurve,
      updateDecayMode,
      rewards,
      addReward,
      updateReward,
      removeReward,
      flagTemplateValue,
      updateFlagTemplate,
      PointsDecayCurve
    }
}

export type ConfigFieldInputViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useConfigFieldInput>>>
