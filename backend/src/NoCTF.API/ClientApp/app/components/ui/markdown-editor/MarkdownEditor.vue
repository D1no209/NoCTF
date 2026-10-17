<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { EditorState, Compartment } from '@codemirror/state'
import { EditorView, keymap, placeholder as placeholderExtension } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { Code, Heading2, Link, List } from '@lucide/vue'
import { livePreview } from './live-preview'

const props = defineProps<{ disabled?: boolean; label: string; placeholder?: string }>()
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
// 外部模型变化(表单重置/加载)才写回编辑器;必须等组件更新后再 dispatch,
// 否则 CodeMirror 会在 DOM 补丁期间测量布局并抛错。
watch(model, (value) => {
  if (!view || value === view.state.doc.toString()) return
  external = true
  try { view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } }) }
  catch { /* 编辑器正在销毁或处于非可编辑状态,忽略此次外部写入 */ }
  finally { external = false }
}, { flush: 'post' })
watch(() => props.disabled, syncEditable, { flush: 'post' })
onBeforeUnmount(() => { view?.destroy(); view = null })
</script>

<template>
  <section data-slot="markdown-editor" class="min-w-0 space-y-3" :aria-label="label">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex gap-1">
        <Button v-for="action in actions" :key="action.key" type="button" variant="ghost" size="icon-sm" :disabled="disabled" :aria-label="$t(`markdown.editor.${action.key}`)" @click="insert(action.text)"><component :is="action.icon" /></Button>
      </div>
    </div>
    <div ref="host" data-slot="markdown-editor-surface" class="min-h-64 rounded-[10px] bg-[var(--field-surface)] p-2" />
  </section>
</template>

<!-- 编辑器需要阅读态规则来排版实时预览中的表格/围栏块,与 MarkdownContent 共用同一份懒加载样式。 -->
<style src="../markdown/markdown.css"></style>
