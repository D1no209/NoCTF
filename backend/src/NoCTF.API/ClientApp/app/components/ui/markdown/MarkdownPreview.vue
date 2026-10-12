<script setup lang="ts">
import { prefetchMarkdownHighlighter, renderMarkdownPreview } from '~/lib/markdown-preview-worker.client'
import { renderMarkdownAsync } from '~/lib/markdown'
import MarkdownDocument from './MarkdownDocument'

const props = defineProps<{ source: string; label: string; emptyLabel: string; assets?: Readonly<Record<string, string>> }>()

const html = shallowRef('')
let pendingSource = props.source
let previewFrame: number | undefined
let renderSequence = 0

async function renderLatest(source: string) {
  const sequence = ++renderSequence
  const rendered = props.assets === undefined ? await renderMarkdownPreview(source) : await renderMarkdownAsync(source, props.assets)
  if (sequence === renderSequence)
    html.value = rendered
}

watch(() => [props.source, props.assets] as const, ([source]) => {
  pendingSource = source
  if (previewFrame !== undefined) return
  previewFrame = requestAnimationFrame(() => {
    previewFrame = undefined
    void renderLatest(pendingSource)
  })
}, { flush: 'post', immediate: true })

onBeforeUnmount(() => {
  renderSequence++
  if (previewFrame !== undefined) cancelAnimationFrame(previewFrame)
})
</script>

<template>
  <section data-slot="markdown-preview" class="flex min-h-48 min-w-0 flex-col overflow-hidden rounded-lg border" :aria-label="label" @focusin.once="prefetchMarkdownHighlighter">
    <header class="shrink-0 border-b px-4 py-2 text-sm font-medium">{{ label }}</header>
    <ScrollSurface axis="both" class="min-h-0 flex-1">
      <div class="min-h-40 p-4">
        <div v-if="source.trim()" class="markdown-content min-w-0 text-sm leading-7"><MarkdownDocument :html="html" /></div>
        <p v-else class="text-sm text-muted-foreground">{{ emptyLabel }}</p>
      </div>
    </ScrollSurface>
  </section>
</template>

<style src="./markdown.css"></style>
