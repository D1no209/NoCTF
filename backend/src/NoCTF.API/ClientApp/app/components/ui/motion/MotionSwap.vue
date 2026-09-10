<script setup lang="ts">
import { useContentSwap, type ContentSwapPreset } from '~/motion/useContentSwap'

const props = withDefaults(defineProps<{ identity: string | number; preset?: ContentSwapPreset }>(), { preset: 'film-up' })
const { frameClass, frameStyle, transition } = useContentSwap(props.preset)
</script>

<template>
  <div :class="frameClass" :style="frameStyle">
    <Transition v-bind="transition">
      <div :key="identity"><slot /></div>
    </Transition>
  </div>
</template>
