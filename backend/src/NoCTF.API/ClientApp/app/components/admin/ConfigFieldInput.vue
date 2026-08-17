<script setup lang="ts">
import type {
  BloodRewardValue,
  ConfigFieldDef,
  FlagTemplateModel,
  PointsCurveValue,
} from '~/utils/game-config'
import {
  BLOOD_REWARD_POLICIES,
  BloodRewardPolicy,
  emptyFlagTemplate,
  SCORE_DECAY_MODES,
  ScoreDecayMode,
} from '~/utils/game-config'

const props = withDefaults(defineProps<{
  field: ConfigFieldDef
  modelValue: unknown
  disabled?: boolean
}>(), {
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: unknown] }>()

function emitValue(value: unknown) {
  emit('update:modelValue', value)
}

function parseNullableNumber(value: unknown): number | null {
  if (value === '' || value === null || value === undefined) return null
  const n = Number(value)
  return Number.isFinite(n) ? n : null
}

// ---- int / decimal ----
const numberText = computed(() => typeof props.modelValue === 'number' ? String(props.modelValue) : '')

// ---- string / text ----
const textValue = computed(() => typeof props.modelValue === 'string' ? props.modelValue : '')

// ---- bool ----
const boolValue = computed(() => props.modelValue === true)

// ---- select ----
const selectValue = computed(() => {
  const v = props.modelValue
  return typeof v === 'string' || typeof v === 'number' ? v : undefined
})

// ---- pointsCurve ----
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

// ---- bloodRewards ----
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

// ---- flagTemplate ----
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
</script>

<template>
  <Input
    v-if="field.type === 'int' || field.type === 'decimal'"
    type="number"
    :model-value="numberText"
    :min="field.min"
    :max="field.max"
    :step="field.type === 'decimal' ? 'any' : 1"
    :placeholder="field.placeholder"
    :disabled="disabled"
    @update:model-value="emitValue(parseNullableNumber($event))"
  />
  <Input
    v-else-if="field.type === 'string'"
    :model-value="textValue"
    :placeholder="field.placeholder"
    :disabled="disabled"
    @update:model-value="emitValue($event)"
  />
  <Textarea
    v-else-if="field.type === 'text'"
    :model-value="textValue"
    :rows="3"
    :placeholder="field.placeholder"
    :disabled="disabled"
    @update:model-value="emitValue($event)"
  />
  <Switch
    v-else-if="field.type === 'bool'"
    :model-value="boolValue"
    :disabled="disabled"
    @update:model-value="emitValue($event)"
  />
  <Select
    v-else-if="field.type === 'select'"
    :model-value="selectValue"
    :disabled="disabled"
    @update:model-value="emitValue($event)"
  >
    <SelectTrigger class="w-full">
      <SelectValue />
    </SelectTrigger>
    <SelectContent>
      <SelectGroup>
        <SelectItem v-for="option in field.options ?? []" :key="String(option.value)" :value="option.value">
          {{ $t(option.label) }}
        </SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>

  <div v-else-if="field.type === 'pointsCurve'" class="flex flex-col gap-3">
    <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('衰减模式') }}</span>
        <Select
          :model-value="curve.decayMode"
          :disabled="disabled"
          @update:model-value="updateDecayMode"
        >
          <SelectTrigger class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in SCORE_DECAY_MODES" :key="String(option.value)" :value="option.value">
                {{ $t(option.label) }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('初始分') }}</span>
        <Input
          type="number"
          :model-value="curve.initialPoints ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ initialPoints: parseNullableNumber($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('最低分') }}</span>
        <Input
          type="number"
          :model-value="curve.minimumPoints ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ minimumPoints: parseNullableNumber($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('降至最低分时的队伍数') }}</span>
        <Input
          type="number"
          min="2"
          step="1"
          :model-value="curve.decayTeamCount ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ decayTeamCount: parseNullableNumber($event) })"
        />
      </div>
    </div>
    <div v-if="curve.decayMode === ScoreDecayMode.Custom" class="flex flex-col gap-1.5">
      <span class="text-xs text-muted-foreground">{{ $t('自定义衰减公式') }}</span>
      <Textarea
        :model-value="curve.customExpression ?? ''"
        :rows="3"
        class="font-mono"
        :disabled="disabled"
        :placeholder="$t('变量：initialPoints、minimumPoints、solveCount、eligibleTeamCount、decayTeamCount')"
        @update:model-value="updateCurve({ customExpression: String($event) })"
      />
    </div>
    <PointsDecayCurve :curve="curve" />
  </div>

  <div v-else-if="field.type === 'bloodRewards'" class="flex flex-col gap-2">
    <p v-if="rewards.length === 0" class="text-sm text-muted-foreground">{{ $t('无血榜奖励') }}</p>
    <div v-for="(reward, index) in rewards" :key="index" class="flex items-center gap-2">
      <Select
        :model-value="reward.policy"
        :disabled="disabled"
        @update:model-value="updateReward(index, { policy: parseNullableNumber($event) ?? reward.policy })"
      >
        <SelectTrigger class="w-56">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem v-for="option in BLOOD_REWARD_POLICIES" :key="String(option.value)" :value="option.value">
              {{ $t(option.label) }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Input
        type="number"
        step="any"
        class="w-32"
        :model-value="reward.value ?? ''"
        :disabled="disabled"
        @update:model-value="updateReward(index, { value: parseNullableNumber($event) })"
      />
      <Button variant="ghost" size="sm" :disabled="disabled" @click="removeReward(index)"> {{ $t('删除') }} </Button>
    </div>
    <div>
      <Button variant="outline" size="sm" :disabled="disabled || rewards.length >= 3" @click="addReward"> {{ $t('添加奖励') }} </Button>
    </div>
  </div>

  <div v-else-if="field.type === 'flagTemplate'" class="flex flex-col gap-3">
    <div class="grid gap-3 sm:grid-cols-2">
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('前缀(header)') }}</span>
        <Input
          :model-value="flagTemplateValue.header"
          :disabled="disabled"
          @update:model-value="updateFlagTemplate({ header: String($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('正文模板(bodyTemplate)') }}</span>
        <Input
          :model-value="flagTemplateValue.bodyTemplate"
          class="font-mono"
          :disabled="disabled"
          @update:model-value="updateFlagTemplate({ bodyTemplate: String($event) })"
        />
      </div>
    </div>
    <div class="flex items-center gap-2">
      <Switch
        :model-value="flagTemplateValue.leetLiteralText"
        :disabled="disabled"
        @update:model-value="updateFlagTemplate({ leetLiteralText: $event === true })"
      />
      <span class="text-sm text-muted-foreground">{{ $t('leetLiteralText(字面文本转 leet 风格)') }}</span>
    </div>
    <FieldDescription> {{ $t('可用占位符:[GUID]、[TEAMID]、[CHALLENGEID]、[COMPETITIONCHALLENGEID]、[COMPETITIONID]、[TEAMHASH:n]、[RANDOM:n]') }} </FieldDescription>
  </div>
</template>
