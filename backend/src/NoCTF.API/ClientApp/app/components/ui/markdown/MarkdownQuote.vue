<script setup lang="ts">
import MarkdownContent from './MarkdownContent.vue'

defineProps<{ source: string }>()
</script>

<template>
  <blockquote data-slot="markdown-quote">
    <MarkdownContent :source="source" />
  </blockquote>
</template>
