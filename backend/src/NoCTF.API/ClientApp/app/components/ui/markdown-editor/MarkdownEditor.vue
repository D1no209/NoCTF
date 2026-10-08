<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { Code, Heading2, Link, List } from '@lucide/vue'

const props = defineProps<{ disabled?: boolean; label: string; placeholder?: string }>()
const model = defineModel<string>({ default: '' })
const emit = defineEmits<{ save: [] }>()
const root = ref<HTMLElement | null>(null)
const layout = ref('split')
const narrow = useMediaQuery('(max-width: 767px)')
const effectiveLayout = computed(() => narrow.value && layout.value === 'split' ? 'edit' : layout.value)
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
function setLayout(value: unknown) { if (value === 'edit' || value === 'preview' || value === 'split') layout.value = value }
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
      <ToggleGroup type="single" :model-value="layout" :aria-label="$t('challengeWriteUp.editorLayout')" @update:model-value="setLayout">
        <ToggleGroupItem value="edit">{{ $t('challengeWriteUp.edit') }}</ToggleGroupItem><ToggleGroupItem value="preview">{{ $t('challengeWriteUp.preview') }}</ToggleGroupItem><ToggleGroupItem v-if="!narrow" value="split">{{ $t('challengeWriteUp.split') }}</ToggleGroupItem>
      </ToggleGroup>
    </div>
    <div class="grid min-w-0 gap-4" :class="effectiveLayout === 'split' ? 'md:grid-cols-2' : ''">
      <Textarea v-if="effectiveLayout !== 'preview'" v-model="model" :disabled="disabled" :aria-label="label" :placeholder="placeholder" class="min-h-64" />
      <MarkdownPreview v-if="effectiveLayout !== 'edit'" :source="model" :label="$t('challengeWriteUp.preview')" :empty-label="$t('challengeWriteUp.previewEmpty')" />
    </div>
  </section>
</template>
