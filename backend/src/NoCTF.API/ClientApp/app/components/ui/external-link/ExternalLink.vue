<script setup lang="ts">
withDefaults(defineProps<{ href?: string; target?: string; rel?: string }>(), {
  target: '_blank',
  rel: 'noopener noreferrer',
})
</script>

<template>
  <a :href="href" :target="target" :rel="rel"><slot /></a>
</template>
