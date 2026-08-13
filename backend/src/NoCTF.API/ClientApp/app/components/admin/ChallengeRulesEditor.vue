<script setup lang="ts">
import type { ConfigFieldDef, ConfigValues, GameModeValue } from '~/utils/game-config'
import { challengeRuleFields, parseConfigValues, serializeConfigValues } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  /** 服务器端当前规则 JSON。 */
  json?: string | null
  /** 当前竞赛配置 JSON，用于展示继承后的具体值。 */
  inheritedJson?: string | null
  /** 乐观并发修订版本(仅展示)。 */
  revision?: number
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}>(), {
  json: null,
  inheritedJson: null,
  revision: 0,
  readonly: false,
  loading: false,
  saving: false,
})

const emit = defineEmits<{ save: [json: string] }>()

const fields = computed(() => challengeRuleFields(props.mode))
const values = ref<ConfigValues>({})
const overridden = ref<Record<string, boolean>>({})
const parseFailed = ref(false)
const inheritedValues = computed(() =>
  parseConfigValues(props.inheritedJson, fields.value)?.values ?? {},
)

watch(
  [() => props.json, () => props.mode],
  () => {
    const parsed = parseConfigValues(props.json, fields.value)
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

const serialized = computed(() =>
  serializeConfigValues(props.mode, fields.value, values.value, { rules: true, overridden: overridden.value }),
)

const dirty = computed(() => {
  if (parseFailed.value) return false
  const original = normalize(props.json ?? '')
  if (original === null) return true
  return normalize(serialized.value) !== original
})

function save() {
  emit('save', serialized.value)
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex items-center gap-2 text-xs text-muted-foreground">
      <span>{{ $t('修订版本：{revision}', { revision }) }}</span>
      <span v-if="dirty">{{ $t('· 有未保存的修改') }}</span>
    </div>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <Alert v-else-if="parseFailed" variant="destructive">
      <AlertDescription>{{ $t('配置 JSON 无法解析,请联系平台管理员修复') }}</AlertDescription>
    </Alert>
    <template v-else>
      <FieldGroup>
        <Field v-for="field in fields" :key="field.key">
          <div class="flex items-center gap-2">
            <FieldLabel>{{ field.label }}</FieldLabel>
            <Switch
              size="sm"
              :model-value="overridden[field.key] ?? false"
              :disabled="readonly"
              @update:model-value="setOverride(field, $event === true)"
            />
            <span class="text-xs text-muted-foreground">{{ $t('覆盖') }}</span>
            <span v-if="!(overridden[field.key] ?? false)" class="text-xs text-muted-foreground">{{ $t('· 继承竞赛默认') }}</span>
          </div>
          <ConfigFieldInput
            :field="field"
            :model-value="displayedValue(field)"
            :disabled="readonly || !(overridden[field.key] ?? false)"
            @update:model-value="updateField(field.key, $event)"
          />
          <FieldDescription v-if="field.description">{{ field.description }}</FieldDescription>
        </Field>
      </FieldGroup>
      <div v-if="!readonly">
        <Button :disabled="saving || !dirty" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('保存配置') }} </Button>
      </div>
    </template>
  </div>
</template>
