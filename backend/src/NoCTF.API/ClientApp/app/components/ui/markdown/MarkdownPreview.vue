<script setup lang="ts">
const props = defineProps<{ source: string; label: string; emptyLabel: string }>()

const previewSource = shallowRef(props.source)
let pendingSource = props.source
let previewFrame: number | undefined

watch(() => props.source, (source) => {
  pendingSource = source
  if (previewFrame !== undefined) return
  previewFrame = requestAnimationFrame(() => {
    previewFrame = undefined
    previewSource.value = pendingSource
  })
}, { flush: 'post' })

onBeforeUnmount(() => {
  if (previewFrame !== undefined) cancelAnimationFrame(previewFrame)
})
</script>

<template>
  <section data-slot="markdown-preview" class="flex min-h-48 min-w-0 flex-col overflow-hidden rounded-lg border" :aria-label="label">
    <header class="shrink-0 border-b px-4 py-2 text-sm font-medium">{{ label }}</header>
    <ScrollSurface axis="both" class="min-h-0 flex-1">
      <div class="min-h-40 p-4">
        <MarkdownContent v-if="previewSource.trim()" :source="previewSource" />
        <p v-else class="text-sm text-muted-foreground">{{ emptyLabel }}</p>
      </div>
    </ScrollSurface>
  </section>
</template>
