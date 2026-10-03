<script setup lang="ts">
import { computed, ref } from 'vue'
import { X } from '@lucide/vue'

const props = withDefaults(defineProps<{
  modelValue: string[]
  options: string[]
  label: string
  allowCreate?: boolean
  disabled?: boolean
}>(), { allowCreate: false, disabled: false })
const emit = defineEmits<{ 'update:modelValue': [value: string[]] }>()
const search = ref('')
const open = ref(false)
const key = (name: string) => name.trim().toUpperCase()
const selected = (name: string) => props.modelValue.some(value => key(value) === key(name))
const options = computed(() => {
  const unique = new Map<string, string>()
  for (const name of [...props.options, ...props.modelValue]) unique.set(key(name), name)
  return [...unique.values()].filter(name => key(name).includes(key(search.value)))
})
function remove(name: string) {
  emit('update:modelValue', props.modelValue.filter(value => key(value) !== key(name)))
}
function toggle(name: string) {
  if (props.disabled) return
  if (selected(name)) remove(name)
  else emit('update:modelValue', [...props.modelValue, name])
}
function add(event?: KeyboardEvent) {
  if (event?.isComposing || props.disabled || !props.allowCreate || !search.value.trim()) return
  const name = search.value.trim()
  if (!selected(name)) emit('update:modelValue', [...props.modelValue, name])
  search.value = ''
}
function clear() { emit('update:modelValue', []) }
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2" data-slot="tag-picker">
    <Popover v-model:open="open">
      <PopoverTrigger as-child>
        <Button type="button" variant="outline" class="w-full justify-between" :disabled="disabled" :aria-label="label">
          <span class="truncate">{{ label }}</span>
          <Badge v-if="modelValue.length" variant="secondary">{{ modelValue.length }}</Badge>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" class="gap-2">
        <Input v-model="search" :placeholder="$t('challengeTags.search')" :aria-label="$t('challengeTags.search')" @keydown.enter.prevent="add" />
        <Button v-if="allowCreate && search.trim()" type="button" variant="secondary" :disabled="disabled" @click="add()">{{ $t('challengeTags.add', { name: search.trim() }) }}</Button>
        <ScrollSurface class="max-h-56">
          <div class="flex flex-col gap-1">
            <Button v-for="name in options" :key="key(name)" type="button" variant="ghost" class="h-auto min-h-10 justify-start whitespace-normal text-left" :disabled="disabled" :aria-pressed="selected(name)" @click="toggle(name)">
              <span class="min-w-0 flex-1 break-words">{{ name }}</span>
              <span v-if="selected(name)" aria-hidden="true">✓</span>
            </Button>
            <p v-if="!options.length" class="p-2 text-sm text-muted-foreground">{{ $t('challengeTags.noOptions') }}</p>
          </div>
        </ScrollSurface>
      </PopoverContent>
    </Popover>
    <div v-if="modelValue.length" class="flex flex-wrap items-center gap-1">
      <Button v-for="name in modelValue" :key="key(name)" type="button" variant="secondary" size="sm" class="h-auto min-h-8 max-w-full" :disabled="disabled" :aria-label="$t('challengeTags.remove', { name })" @click="remove(name)">
        <span class="min-w-0 break-words whitespace-normal">{{ name }}</span><X class="size-3 shrink-0" />
      </Button>
      <Button type="button" variant="ghost" size="sm" :disabled="disabled" @click="clear">{{ $t('challengeTags.clear') }}</Button>
    </div>
  </div>
</template>
