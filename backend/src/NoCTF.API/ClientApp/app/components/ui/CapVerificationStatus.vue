<script setup lang="ts">
import { toRef } from 'vue'
import { useCapVerificationMotion } from '~/motion/useCapVerificationMotion'
import type { CapVerificationVisualState } from '~/motion/useCapVerificationMotion'

const props = defineProps<{
  state: CapVerificationVisualState
  progress: number
  label: string
}>()
const { bits, characters, flashEpochs, hotIndices, settled } = useCapVerificationMotion(toRef(props, 'state'))
</script>

<template>
  <div
    data-slot="cap-verification-status"
    :data-state="state"
    :data-settled="settled || undefined"
    :role="state === 'running' ? 'progressbar' : 'status'"
    :aria-label="label"
    :aria-valuemin="state === 'running' ? 0 : undefined"
    :aria-valuemax="state === 'running' ? 100 : undefined"
    :aria-valuenow="state === 'running' ? progress : undefined"
    aria-live="polite"
  >
    <div data-slot="cap-bitstream" aria-hidden="true">
      <span
        v-for="bit in bits"
        :key="bit.id"
        data-slot="cap-bit"
        :data-strong="bit.strong || undefined"
        :style="bit.style"
      >{{ bit.value }}</span>
    </div>
    <div data-slot="cap-visual" aria-hidden="true">
      <span data-slot="cap-outer-ring" />
      <span data-slot="cap-inner-ring" />
      <span data-slot="cap-scan-ring" />
      <span v-for="ray in 5" :key="ray" data-slot="cap-ray" :style="{ '--cap-ray': ray - 1 }" />
      <span data-slot="cap-hex" />
      <span data-slot="cap-core" />
      <span data-slot="cap-check" />
    </div>
    <div data-slot="cap-content" aria-hidden="true">
      <div data-slot="cap-status-label">{{ label }}</div>
      <div data-slot="cap-hash">
        <span
          v-for="(character, index) in characters"
          :key="`${index}-${flashEpochs[index]}`"
          data-slot="cap-hash-character"
          :data-hot="hotIndices.includes(index) || undefined"
          :data-leading="state === 'success' && index < 5 || undefined"
        >{{ character }}</span>
      </div>
    </div>
  </div>
</template>
