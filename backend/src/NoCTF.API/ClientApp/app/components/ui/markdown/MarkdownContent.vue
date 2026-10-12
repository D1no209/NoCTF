<script setup lang="ts">
import { renderPublishedMarkdown, renderMarkdownAsync } from '~/lib/markdown'
import MarkdownDocument from './MarkdownDocument'

const props = defineProps<{ source: string; assets?: Readonly<Record<string, string>> }>()
const html = shallowRef('')
let renderSequence = 0

watch(() => [props.source, props.assets] as const, async ([source, assets]) => {
  const sequence = ++renderSequence
  const rendered = assets === undefined ? await renderPublishedMarkdown(source) : await renderMarkdownAsync(source, assets)
  if (sequence === renderSequence)
    html.value = rendered
}, { immediate: true })

onBeforeUnmount(() => { renderSequence++ })
</script>

<template>
  <div class="markdown-content min-w-0 text-sm leading-7"><MarkdownDocument :html="html" /></div>
</template>

<style src="./markdown.css"></style>
