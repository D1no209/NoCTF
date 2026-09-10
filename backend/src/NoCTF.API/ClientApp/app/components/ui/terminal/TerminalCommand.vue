<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { motionAttributes } from '~/motion/presets'
import { terminalEnterAction } from './terminal-keys'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'
import { Spinner } from '../spinner'

const props = withDefaults(defineProps<{ modelValue: string; label: string; id?: string; multiple?: boolean; disabled?: boolean; pending?: boolean; hint?: string; placeholder?: string }>(), { multiple: false, placeholder: '' })
const emit = defineEmits<{ 'update:modelValue': [value: string]; submit: [] }>()
const editor = ref<HTMLTextAreaElement | null>(null)
const focused = ref(false)
const composing = ref(false)
const blocked = computed(() => Boolean(props.disabled || props.pending))
function focusEditor() { if (!props.disabled) editor.value?.focus({ preventScroll: true }) }
function resize() {
  if (!editor.value) return
  editor.value.style.height = 'auto'
  editor.value.style.height = `${Math.max(28, editor.value.scrollHeight)}px`
}
watch(() => props.modelValue, () => void nextTick(resize), { immediate: true })
function update(event: Event) {
  if (!composing.value) emit('update:modelValue', (event.target as HTMLTextAreaElement).value)
  resize()
}
function compositionEnd(event: CompositionEvent) { composing.value = false; update(event) }
function keydown(event: KeyboardEvent) {
  const action = terminalEnterAction(event, { composing: composing.value, multiple: props.multiple, blocked: blocked.value, value: props.modelValue })
  if (action === 'ignore' || action === 'newline') return
  event.preventDefault()
  if (action === 'submit') emit('submit')
}
</script>

<template>
  <div data-slot="terminal-command" :aria-busy="pending" :data-disabled="disabled || undefined" @click="focusEditor">
    <span data-slot="terminal-prompt" aria-hidden="true">&gt;_</span>
    <ScrollSurface axis="y" class="min-w-0 flex-1 max-h-40" :aria-label="label">
      <div class="relative">
        <textarea ref="editor" :id="id" data-slot="terminal-editor" :value="modelValue" :placeholder="placeholder" :aria-label="label" :aria-multiline="multiple" :aria-description="hint" :readonly="blocked" :aria-disabled="disabled || undefined" rows="1" autocomplete="off" autocapitalize="off" :spellcheck="false"
          @input="update" @keydown="keydown" @compositionstart="composing = true" @compositionend="compositionEnd" @focus="focused = true" @blur="focused = false" />
        <span v-if="!modelValue && !focused && !blocked && !placeholder" v-bind="motionAttributes('terminal-caret')" data-slot="terminal-caret" aria-hidden="true">|</span>
      </div>
    </ScrollSurface>
    <Spinner v-if="pending" class="mt-1 size-4 shrink-0 text-primary" />
  </div>
</template>
