<script setup lang="ts">
import type { ConfigValues, GameModeValue } from '~/utils/game-config'
import { competitionConfigFields, parseConfigValues, serializeConfigValues } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  mode: GameModeValue
  /** 服务器端当前配置 JSON。 */
  json?: string | null
  /** 乐观并发修订版本(仅展示)。 */
  revision?: number
  readonly?: boolean
  loading?: boolean
  saving?: boolean
}>(), {
  json: null,
  revision: 0,
  readonly: false,
  loading: false,
  saving: false,
})

const emit = defineEmits<{ save: [json: string] }>()

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
          <FieldLabel>{{ field.label }}</FieldLabel>
          <ConfigFieldInput
            :field="field"
            :model-value="values[field.key]"
            :disabled="readonly"
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
