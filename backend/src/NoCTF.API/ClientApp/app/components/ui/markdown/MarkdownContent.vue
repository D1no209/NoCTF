<script setup lang="ts">
import { renderPublishedMarkdown } from '~/lib/markdown'
import MarkdownDocument from './MarkdownDocument'

const props = defineProps<{ source: string }>()
const html = shallowRef('')
let renderSequence = 0

watch(() => props.source, async (source) => {
  const sequence = ++renderSequence
  const rendered = await renderPublishedMarkdown(source)
  if (sequence === renderSequence)
    html.value = rendered
}, { immediate: true })

onBeforeUnmount(() => { renderSequence++ })
</script>

<template>
  <div class="markdown-content min-w-0 text-sm leading-7"><MarkdownDocument :html="html" /></div>
</template>

<style src="./markdown.css"></style>
