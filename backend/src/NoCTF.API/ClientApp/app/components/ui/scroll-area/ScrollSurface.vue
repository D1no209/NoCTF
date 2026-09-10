<script setup lang="ts">
withDefaults(defineProps<{ as?: 'div' | 'section' | 'main' | 'nav' | 'ul' | 'ol' | 'pre' | 'code' | 'aside' | 'article'; axis?: 'x' | 'y' | 'both' }>(), { as: 'div', axis: 'both' })
</script>

<template>
  <component :is="as" tabindex="0" data-slot="scroll-surface" data-scroll-surface :data-scroll-axis="axis"><slot /></component>
</template>
