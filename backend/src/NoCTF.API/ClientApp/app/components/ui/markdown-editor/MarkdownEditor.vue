<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { Code, Heading2, Link, List } from '@lucide/vue'

const props = defineProps<{ disabled?: boolean; label: string; placeholder?: string; assets?: Readonly<Record<string, string>> }>()
const model = defineModel<string>({ default: '' })
const emit = defineEmits<{ save: [] }>()
const root = ref<HTMLElement | null>(null)
async function insert(kind: 'heading' | 'list' | 'code' | 'link') {
  const input = root.value?.querySelector('textarea')
  const start = input?.selectionStart ?? model.value.length
  const end = input?.selectionEnd ?? start
  const selected = model.value.slice(start, end)
  const syntax = { heading: `\n## ${selected}\n`, list: `\n- ${selected}\n`, code: `\n\`\`\`\n${selected}\n\`\`\`\n`, link: `[${selected}](https://)` }[kind]
  model.value = model.value.slice(0, start) + syntax + model.value.slice(end)
  await nextTick()
  input?.focus()
  input?.setSelectionRange(start + syntax.length, start + syntax.length)
}
function keydown(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's' && !props.disabled) {
    event.preventDefault(); emit('save')
  }
}
</script>

<template>
  <section ref="root" data-slot="markdown-editor" class="min-w-0 space-y-3" :aria-label="label" @keydown="keydown">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex gap-1">
        <Button type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t('challengeWriteUp.heading')" @click="insert('heading')"><Heading2 /></Button>
        <Button type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t('challengeWriteUp.list')" @click="insert('list')"><List /></Button>
        <Button type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t('challengeWriteUp.code')" @click="insert('code')"><Code /></Button>
        <Button type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t('challengeWriteUp.link')" @click="insert('link')"><Link /></Button>
      </div>
      <span class="text-xs text-muted-foreground">{{ $t('challengeWriteUp.preview') }}</span>
    </div>
    <div class="grid min-w-0 gap-4 md:grid-cols-2">
      <Textarea v-model="model" :disabled="disabled" :aria-label="label" :placeholder="placeholder" class="min-h-64" />
      <MarkdownPreview :source="model" :assets="assets" :label="$t('challengeWriteUp.preview')" :empty-label="$t('challengeWriteUp.previewEmpty')" class="min-h-64" />
    </div>
  </section>
</template>
