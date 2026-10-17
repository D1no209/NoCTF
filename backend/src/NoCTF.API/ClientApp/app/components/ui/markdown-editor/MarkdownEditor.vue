<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { EditorState, Compartment } from '@codemirror/state'
import { EditorView, keymap, placeholder as placeholderExtension } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { Code, Heading2, Link, List } from '@lucide/vue'
import { livePreview, livePreviewTheme } from './live-preview'

const props = defineProps<{ disabled?: boolean; label: string; placeholder?: string; assets?: Readonly<Record<string, string>> }>()
const model = defineModel<string>({ default: '' })
const emit = defineEmits<{ save: [] }>()
const host = ref<HTMLElement | null>(null)
const editable = new Compartment()
let view: EditorView | null = null
let external = false

function syncEditable() { view?.dispatch({ effects: editable.reconfigure(EditorView.editable.of(!props.disabled)) }) }
function insert(text: string) {
  if (!view || props.disabled) return
  view.dispatch(view.state.replaceSelection(text))
  view.focus()
}
const actions = [
  { key: 'heading', icon: Heading2, text: '\n## ' },
  { key: 'list', icon: List, text: '\n- ' },
  { key: 'code', icon: Code, text: '\n\`\`\`\n' },
  { key: 'link', icon: Link, text: '[](https://)' },
] as const

onMounted(() => {
  if (!host.value) return
  view = new EditorView({
    parent: host.value,
    state: EditorState.create({
      doc: model.value,
      extensions: [
        history(),
        markdown(),
        EditorView.lineWrapping,
        livePreview,
        livePreviewTheme,
        editable.of(EditorView.editable.of(!props.disabled)),
        placeholderExtension(props.placeholder ?? ''),
        keymap.of([{ key: 'Mod-s', preventDefault: true, run: () => { if (!props.disabled) emit('save'); return true } }, indentWithTab, ...defaultKeymap, ...historyKeymap]),
        EditorView.updateListener.of((update) => {
          if (!update.docChanged || external) return
          model.value = update.state.doc.toString()
        }),
      ],
    }),
  })
})
watch(model, (value) => {
  if (!view || value === view.state.doc.toString()) return
  external = true
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } })
  external = false
})
watch(() => props.disabled, syncEditable)
onBeforeUnmount(() => { view?.destroy(); view = null })
</script>

<template>
  <section data-slot="markdown-editor" class="min-w-0 space-y-3" :aria-label="label">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex gap-1">
        <Button v-for="action in actions" :key="action.key" type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t(`challengeWriteUp.${action.key}`)" @click="insert(action.text)"><component :is="action.icon" /></Button>
      </div>
      <span class="text-xs text-muted-foreground">{{ $t('challengeWriteUp.preview') }}</span>
    </div>
    <div ref="host" data-slot="markdown-editor-surface" class="min-h-64 rounded-[10px] bg-[var(--field-surface)] p-2" />
  </section>
</template>
