<script setup lang="ts">
import { computed } from 'vue'
import CommandButton from './CommandButton.vue'
import CommandPanel from './CommandPanel.vue'
import CommandSignal from './CommandSignal.vue'

const props = withDefaults(defineProps<{
  open: boolean
  signalLabel?: string
  signalTone?: 'info' | 'success' | 'warning' | 'danger'
  title: string
  width?: string
}>(), {
  signalLabel: undefined,
  signalTone: 'info',
  width: '560px',
})

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const isOpen = computed({
  get: () => props.open,
  set: value => emit('update:open', value),
})
</script>

<template>
  <Teleport to="body">
    <div v-if="isOpen" class="command-dialog ui-v2" role="dialog" aria-modal="true" :aria-label="props.title">
      <button class="command-dialog__backdrop" aria-label="Close dialog" @click="isOpen = false" />
      <CommandPanel class="command-dialog__panel" tone="signal" :style="{ width: `min(${props.width}, 100%)` }">
        <header class="command-dialog__header">
          <div>
            <CommandSignal v-if="props.signalLabel" :label="props.signalLabel" :tone="props.signalTone" />
            <h2>{{ props.title }}</h2>
          </div>
          <CommandButton label="Close" tone="ghost" @click="isOpen = false" />
        </header>
        <div class="command-dialog__body">
          <slot />
        </div>
        <footer v-if="$slots.footer" class="command-dialog__footer">
          <slot name="footer" />
        </footer>
      </CommandPanel>
    </div>
  </Teleport>
</template>

<style scoped>
/* teleported to <body>: re-declares .ui-v2 for token scope, so the layout
   side-effects of the root class must be neutralized (see styles/index.css) */
.command-dialog { position: fixed; z-index: 50; inset: 0; display: grid; min-height: 0; place-items: center; overflow: visible; padding: 18px; background: transparent; }
.command-dialog__backdrop { position: absolute; inset: 0; border: 0; background: rgb(31 41 55 / 0.38); cursor: default; }
.command-dialog__panel { position: relative; z-index: 1; max-height: min(760px, calc(100dvh - 36px)); overflow-y: auto; }
.command-dialog__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; padding: 18px 18px 4px; }
.command-dialog__header h2 { margin: 8px 0 0; color: var(--v2-text); font-size: 18px; font-weight: 600; }
.command-dialog__body { display: grid; gap: 14px; padding: 14px 18px; }
.command-dialog__footer { display: flex; justify-content: flex-end; gap: 10px; padding: 0 18px 18px; }
</style>
