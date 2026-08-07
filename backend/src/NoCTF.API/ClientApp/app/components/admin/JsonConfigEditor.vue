<script setup lang="ts">
const props = withDefaults(defineProps<{
  /** Current server-side JSON string. */
  json?: string | null
  /** Current optimistic-concurrency revision (display only). */
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

const text = ref('')
const parseError = ref<string | null>(null)

function pretty(source: string): string {
  try {
    return JSON.stringify(JSON.parse(source), null, 2)
  }
  catch {
    return source
  }
}

watch(
  () => props.json,
  (value) => {
    text.value = value ? pretty(value) : '{}'
    parseError.value = null
  },
  { immediate: true },
)

const dirty = computed(() => text.value !== (props.json ? pretty(props.json) : '{}'))

function validate(): boolean {
  try {
    JSON.parse(text.value)
    parseError.value = null
    return true
  }
  catch (e) {
    parseError.value = e instanceof Error ? e.message : 'JSON 格式无效'
    return false
  }
}

function format() {
  if (!validate()) return
  text.value = pretty(text.value)
}

function save() {
  if (!validate()) return
  emit('save', text.value)
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex items-center gap-2 text-xs text-muted-foreground">
      <span>修订版本:{{ revision }}</span>
      <span v-if="dirty">· 有未保存的修改</span>
    </div>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <template v-else>
      <Textarea
        v-model="text"
        class="min-h-64 font-mono text-xs"
        :readonly="readonly"
        spellcheck="false"
        @blur="validate"
      />
      <FieldError v-if="parseError">{{ parseError }}</FieldError>
      <div v-if="!readonly" class="flex items-center gap-2">
        <Button variant="outline" size="sm" :disabled="saving" @click="format">
          格式化
        </Button>
        <Button size="sm" :disabled="saving || !dirty" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" />
          保存配置
        </Button>
      </div>
    </template>
  </div>
</template>
