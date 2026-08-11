<script setup lang="ts">
import { Plus, X } from '@lucide/vue'

const props = withDefaults(defineProps<{
  modelValue: Record<string, string>
  keyPlaceholder?: string
  valuePlaceholder?: string
  addLabel?: string
  disabled?: boolean
}>(), {
  keyPlaceholder: '键',
  valuePlaceholder: '值',
  addLabel: '添加一项',
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: Record<string, string>] }>()

interface Row {
  key: string
  value: string
}

/** 内部行模型:允许输入中的临时空键,提交时过滤。 */
const rows = ref<Row[]>([])

function rowsToObject(): Record<string, string> {
  const next: Record<string, string> = {}
  for (const row of rows.value) {
    const key = row.key.trim()
    if (key) next[key] = row.value
  }
  return next
}

function sameMap(a: Record<string, string>, b: Record<string, string>): boolean {
  const aKeys = Object.keys(a)
  const bKeys = Object.keys(b)
  return aKeys.length === bKeys.length && aKeys.every(key => a[key] === b[key])
}

watch(
  () => props.modelValue,
  (value) => {
    // 自身提交引起的回灌跳过重置,避免打断输入。
    if (sameMap(value, rowsToObject())) return
    rows.value = Object.entries(value).map(([key, v]) => ({ key, value: v }))
  },
  { immediate: true, deep: true },
)

function commit(): void {
  const next = rowsToObject()
  if (sameMap(next, props.modelValue)) return
  emit('update:modelValue', next)
}

function add(): void {
  rows.value.push({ key: '', value: '' })
}

function remove(index: number): void {
  rows.value.splice(index, 1)
  commit()
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-for="(row, index) in rows" :key="index" class="flex items-center gap-2">
      <Input
        v-model="row.key"
        :placeholder="$t(keyPlaceholder)"
        :disabled="disabled"
        class="font-mono text-sm"
        @blur="commit"
      />
      <Input
        v-model="row.value"
        :placeholder="$t(valuePlaceholder)"
        :disabled="disabled"
        class="font-mono text-sm"
        @blur="commit"
      />
      <Button
        v-if="!disabled"
        type="button"
        variant="ghost"
        size="icon"
        class="shrink-0"
        @click="remove(index)"
      >
        <X class="size-4" />
      </Button>
    </div>
    <Button
      v-if="!disabled"
      type="button"
      variant="outline"
      size="sm"
      class="w-fit"
      @click="add"
    >
      <Plus data-icon="inline-start" />
      {{ $t(addLabel) }}
    </Button>
  </div>
</template>
