<script setup lang="ts">
import { computed, ref } from 'vue'
import { useTypewriterMotion } from '~/motion/useTypewriterMotion'
import { terminalEnterAction } from './terminal-keys'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'

type PseudoTerminalLine = {
  label?: string
  value: string
}

const props = withDefaults(defineProps<{
  modelValue: string
  prompt: string
  inputLabel: string
  lines: PseudoTerminalLine[]
  pending?: boolean
}>(), { pending: false })

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: []
}>()

const editor = ref<HTMLInputElement | null>(null)
const composing = ref(false)
const outputTexts = computed(() => props.lines.flatMap(line => line.label ? [line.label, line.value] : [line.value]))
const { frames } = useTypewriterMotion(() => outputTexts.value, () => !props.pending)
const animatedLines = computed(() => {
  let frameIndex = 0
  return props.lines.map(line => ({
    label: line.label ? frames.value[frameIndex++] : undefined,
    value: frames.value[frameIndex++],
  }))
})

function focusEditor() {
  editor.value?.focus({ preventScroll: true })
}

function update(event: Event) {
  if (!composing.value) emit('update:modelValue', (event.target as HTMLInputElement).value)
}

function compositionEnd(event: CompositionEvent) {
  composing.value = false
  update(event)
}

function keydown(event: KeyboardEvent) {
  const action = terminalEnterAction(event, {
    composing: composing.value,
    multiple: false,
    blocked: props.pending,
    value: props.modelValue,
  })
  if (action === 'ignore') return
  event.preventDefault()
  if (action === 'submit') emit('submit')
}
</script>

<template>
  <section data-slot="pseudo-terminal" :aria-label="inputLabel" :aria-busy="pending" @click="focusEditor">
    <label data-slot="pseudo-terminal-input-row">
      <span data-slot="pseudo-terminal-prompt" aria-hidden="true">{{ prompt }}</span>
      <span data-slot="pseudo-terminal-editor">
        <input
          ref="editor"
          :value="modelValue"
          :aria-label="inputLabel"
          :readonly="pending"
          :data-empty="!modelValue || undefined"
          autocomplete="off"
          autocapitalize="none"
          :spellcheck="false"
          maxlength="64"
          @input="update"
          @keydown="keydown"
          @compositionstart="composing = true"
          @compositionend="compositionEnd"
        >
        <span v-if="!modelValue && !pending" data-slot="pseudo-terminal-caret" class="noctf-motion-terminal-caret" aria-hidden="true">|</span>
      </span>
    </label>

    <div class="sr-only" aria-live="polite" aria-atomic="true">
      <div v-for="(line, index) in lines" :key="`${index}:${line.label ?? ''}:${line.value}`">
        <span v-if="line.label">{{ line.label }} </span>{{ line.value }}
      </div>
    </div>
    <ScrollSurface axis="y" data-slot="pseudo-terminal-output" aria-hidden="true">
      <div data-slot="pseudo-terminal-lines">
        <div v-for="(line, index) in animatedLines" :key="`${index}:${line.label?.text ?? ''}:${line.value?.text ?? ''}`" data-slot="pseudo-terminal-line" :data-labelled="line.label ? true : undefined">
          <span v-if="line.label" data-slot="pseudo-terminal-label" class="relative">
            <span class="invisible">{{ line.label.text }}</span>
            <span class="absolute inset-0">{{ line.label.visible }}<span v-if="line.label.active" class="noctf-motion-terminal-caret">|</span></span>
          </span>
          <span v-if="line.value" data-slot="pseudo-terminal-value" class="relative">
            <span class="invisible">{{ line.value.text }}</span>
            <span class="absolute inset-0">{{ line.value.visible }}<span v-if="line.value.active" class="noctf-motion-terminal-caret">|</span></span>
          </span>
        </div>
      </div>
    </ScrollSurface>
  </section>
</template>
