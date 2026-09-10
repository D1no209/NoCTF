<script setup lang="ts">
import { computed } from 'vue'
import { renderMarkdown } from '~/lib/markdown'

const props = defineProps<{ source: string }>()
const text = computed(() => {
  const body = new DOMParser().parseFromString(renderMarkdown(props.source), 'text/html').body
  body.querySelectorAll('p, li, h1, h2, h3, h4, h5, h6, br, pre, td, th').forEach(element => element.append(' '))
  return body.textContent?.replace(/\s+/g, ' ').trim() ?? ''
})
</script>

<template>
  <span>{{ text }}</span>
</template>
