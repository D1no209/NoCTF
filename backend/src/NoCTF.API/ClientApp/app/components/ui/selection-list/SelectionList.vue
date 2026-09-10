<script setup lang="ts" generic="T extends { value: string; label: string }">
import { RovingFocusGroup, RovingFocusItem } from 'reka-ui'
import { motionAttributes } from '~/motion/presets'
import { Button } from '../button'

defineProps<{ items: T[]; modelValue: string | null; label: string; controls?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <RovingFocusGroup orientation="vertical" loop role="listbox" :aria-label="label" data-slot="selection-list">
    <RovingFocusItem v-for="(item, index) in items" :key="item.value" as-child :active="modelValue === item.value" focusable>
      <Button
        v-bind="motionAttributes('list-enter', index)"
        variant="ghost"
        role="option"
        :aria-selected="modelValue === item.value"
        :aria-label="item.label"
        :aria-controls="controls"
        data-slot="selection-list-item"
        class="noctf-motion-interactive"
        @click="emit('update:modelValue', item.value)"
      ><slot :item="item"><span>{{ item.label }}</span></slot></Button>
    </RovingFocusItem>
  </RovingFocusGroup>
</template>
