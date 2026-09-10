<script setup lang="ts">
import { motionAttributes, type MotionPreset } from '~/motion/presets'

withDefaults(defineProps<{ preset?: MotionPreset }>(), { preset: 'detail-enter' })
</script>

<template>
  <div v-bind="motionAttributes(preset)"><slot /></div>
</template>
