<script setup lang="ts">
import { toRefs } from 'vue'
import type { ConfigFieldInputViewState } from '~/features/admin/useConfigFieldInput'

const viewProps = defineProps<{ state: ConfigFieldInputViewState }>()
const { BLOOD_REWARD_POLICIES, SCORE_DECAY_MODES, ScoreDecayMode, emitValue, parseNullableNumber, numberText, textValue, boolValue, selectValue, curve, updateCurve, updateDecayMode, rewards, addReward, updateReward, removeReward, flagTemplateValue, updateFlagTemplate, PointsDecayCurve, field, disabled } = toRefs(viewProps.state)
</script>

<template>
  <NumberInput
    v-if="field.type === 'int' || field.type === 'decimal'"

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
        <span class="text-xs text-muted-foreground">{{ $t('ui.decayMode') }}</span>
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
        <span class="text-xs text-muted-foreground">{{ $t('ui.initialScore') }}</span>
        <NumberInput

          :model-value="curve.initialPoints ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ initialPoints: parseNullableNumber($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('ui.lowestScore') }}</span>
        <NumberInput

          :model-value="curve.minimumPoints ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ minimumPoints: parseNullableNumber($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('ui.teamsAtMinimumScore') }}</span>
        <NumberInput

          min="2"
          step="1"
          :model-value="curve.decayTeamCount ?? ''"
          :disabled="disabled"
          @update:model-value="updateCurve({ decayTeamCount: parseNullableNumber($event) })"
        />
      </div>
    </div>
    <div v-if="curve.decayMode === ScoreDecayMode.Custom" class="flex flex-col gap-1.5">
      <span class="text-xs text-muted-foreground">{{ $t('ui.customDecayFormula') }}</span>
      <Textarea
        :model-value="curve.customExpression ?? ''"
        :rows="3"
        class="font-mono"
        :disabled="disabled"
        :placeholder="$t('ui.variablesInitialpointsMinimumpointsSolvecountEligibleteamcountDecayteamcount')"
        @update:model-value="updateCurve({ customExpression: String($event) })"
      />
    </div>
    <component :is="PointsDecayCurve" :curve="curve" />
  </div>

  <div v-else-if="field.type === 'bloodRewards'" class="flex flex-col gap-2">
    <p v-if="rewards.length === 0" class="text-sm text-muted-foreground">{{ $t('ui.noBloodListReward') }}</p>
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
      <NumberInput

        step="any"
        class="w-32"
        :model-value="reward.value ?? ''"
        :disabled="disabled"
        @update:model-value="updateReward(index, { value: parseNullableNumber($event) })"
      />
      <Button variant="ghost" size="sm" :disabled="disabled" @click="removeReward(index)"> {{ $t('ui.delete') }} </Button>
    </div>
    <div>
      <Button variant="outline" size="sm" :disabled="disabled || rewards.length >= 3" @click="addReward"> {{ $t('ui.addReward') }} </Button>
    </div>
  </div>

  <div v-else-if="field.type === 'flagTemplate'" class="flex flex-col gap-3">
    <div class="grid gap-3 sm:grid-cols-2">
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('ui.prefixHeader') }}</span>
        <Input
          :model-value="flagTemplateValue.header"
          :disabled="disabled"
          @update:model-value="updateFlagTemplate({ header: String($event) })"
        />
      </div>
      <div class="flex flex-col gap-1.5">
        <span class="text-xs text-muted-foreground">{{ $t('ui.bodyTemplateBodytemplate') }}</span>
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
      <span class="text-sm text-muted-foreground">{{ $t('ui.leetliteraltextLiteralTextConvertedToLeetStyle') }}</span>
    </div>
    <FieldDescription> {{ $t('ui.availablePlaceholdersGuidTeamidChallengeidCompetitionchallengeidCompetitionidTeamhashNRandom') }} </FieldDescription>
  </div>
</template>
