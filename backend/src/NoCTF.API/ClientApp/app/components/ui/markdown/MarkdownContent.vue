<script setup lang="ts">
import { renderMarkdown } from '~/lib/markdown'
import MarkdownDocument from './MarkdownDocument'

const props = defineProps<{ source: string }>()
const html = computed(() => renderMarkdown(props.source))
</script>

<template>
  <div class="markdown-content min-w-0 text-sm leading-7"><MarkdownDocument :html="html" /></div>
</template>
