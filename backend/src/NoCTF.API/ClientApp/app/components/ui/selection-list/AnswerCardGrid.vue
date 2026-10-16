<script setup lang="ts" generic="T extends { value: string; number: number; answered: boolean; active: boolean; label: string }">
import { RovingFocusGroup, RovingFocusItem } from 'reka-ui'
import { cn } from '~/lib/utils'
import { Button } from '../button'

/**
 * 题号卡:每行 5 个圆角方块,已答项使用主题色填充,当前题为外发光描边。
 * 只负责渲染与选择转发,答题状态由调用方(功能层)提供。
 */
defineProps<{ items: T[]; label: string; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <RovingFocusGroup orientation="horizontal" loop role="listbox" :aria-label="label" data-slot="answer-card-grid"
    class="grid grid-cols-5 gap-2">
    <RovingFocusItem v-for="item in items" :key="item.value" as-child :active="item.active" focusable>
      <Button
        type="button"
        size="icon"
        :variant="item.answered ? 'default' : 'secondary'"
        :disabled="disabled"
        role="option"
        :aria-selected="item.active"
        :aria-current="item.active ? 'true' : undefined"
        :aria-label="item.label"
        data-slot="answer-card-grid-item"
        :data-answered="item.answered ? 'true' : undefined"
        :class="cn(
          'size-9 rounded-[10px] font-mono text-sm tabular-nums',
          !item.answered && 'bg-muted/40 text-foreground/80',
          item.answered && 'text-primary-foreground',
          item.active && 'shadow-[0_0_0_2px_var(--primary)]',
        )"
        @click="emit('update:modelValue', item.value)"
      >{{ item.number }}</Button>
    </RovingFocusItem>
  </RovingFocusGroup>
</template>
