<script setup lang="ts">
import { renderMarkdown } from '~/lib/markdown'

const props = defineProps<{ source: string }>()
const html = computed(() => renderMarkdown(props.source))
</script>

<template>
  <div class="markdown-content min-w-0 text-sm leading-7" v-html="html" />
</template>

<style scoped>
.markdown-content { max-width: 100%; overflow-x: auto; overflow-wrap: anywhere; }
.markdown-content :deep(p),
.markdown-content :deep(ul),
.markdown-content :deep(ol),
.markdown-content :deep(blockquote),
.markdown-content :deep(pre),
.markdown-content :deep(table) { margin-block: 0.75em; }
.markdown-content :deep(h1),
.markdown-content :deep(h2),
.markdown-content :deep(h3),
.markdown-content :deep(h4),
.markdown-content :deep(h5),
.markdown-content :deep(h6) { margin-block: 1.25em 0.5em; font-weight: 600; line-height: 1.4; }
.markdown-content :deep(h1) { font-size: 1.5rem; }
.markdown-content :deep(h2) { font-size: 1.25rem; }
.markdown-content :deep(h3) { font-size: 1.125rem; }
.markdown-content :deep(h4),
.markdown-content :deep(h5),
.markdown-content :deep(h6) { font-size: 1rem; }
.markdown-content :deep(ul) { list-style: disc; padding-inline-start: 1.5rem; }
.markdown-content :deep(ol) { list-style: decimal; padding-inline-start: 1.5rem; }
.markdown-content :deep(li + li) { margin-top: 0.25em; }
.markdown-content :deep(li > ul),
.markdown-content :deep(li > ol) { margin-block: 0.25em; }
.markdown-content :deep(a) { color: var(--primary); text-decoration: underline; text-underline-offset: 0.2em; }
.markdown-content :deep(blockquote) { border-inline-start: 1px solid var(--border); padding-inline-start: 1rem; color: var(--muted-foreground); }
.markdown-content :deep(code) { font-family: var(--font-mono); font-size: 0.9em; background: var(--muted); border-radius: 0.25rem; padding: 0.15em 0.35em; }
.markdown-content :deep(pre) { max-width: 100%; overflow-x: auto; overflow-wrap: normal; white-space: pre; background: var(--muted); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.875rem; line-height: 1.6; }
.markdown-content :deep(pre code) { padding: 0; border-radius: 0; background: transparent; }
.markdown-content :deep(table) { display: block; max-width: 100%; overflow-x: auto; border-collapse: collapse; }
.markdown-content :deep(th),
.markdown-content :deep(td) { border: 1px solid var(--border); padding: 0.4rem 0.75rem; }
.markdown-content :deep(th) { background: var(--muted); font-weight: 600; }
.markdown-content :deep(img) { display: block; max-width: 100%; height: auto; margin-block: 0.75em; border-radius: var(--radius); }
.markdown-content :deep([style*="text-align:center"] img) { margin-inline: auto; }
.markdown-content :deep(video),
.markdown-content :deep(audio) { display: block; max-width: 100%; margin-block: 0.75em; }
.markdown-content :deep(details) { margin-block: 0.75em; border: 1px solid var(--border); border-radius: var(--radius); padding: 0.75rem; }
.markdown-content :deep(summary) { cursor: pointer; font-weight: 600; }
.markdown-content :deep(hr) { margin-block: 1.25rem; border-color: var(--border); }
.markdown-content :deep(:focus-visible) { outline: 2px solid var(--ring); outline-offset: 2px; }
.markdown-content > :deep(:first-child) { margin-top: 0; }
.markdown-content > :deep(:last-child) { margin-bottom: 0; }
</style>
