<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { EditorState, Compartment } from '@codemirror/state'
import { EditorView, keymap, placeholder as placeholderExtension } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { Code, Heading2, Link, List } from '@lucide/vue'
import { minimalTextChange, shouldApplyExternalModel } from '~/lib/editor-model-sync'
import { livePreview } from './live-preview'

const props = defineProps<{ disabled?: boolean; label: string; placeholder?: string }>()
const model = defineModel<string>({ default: '' })
const emit = defineEmits<{ save: [] }>()
const host = ref<HTMLElement | null>(null)
const editable = new Compartment()
let view: EditorView | null = null
let external = false
/** 最后一次由编辑器自身发给父级的文本:用来识别父级回传的旧值/回声。 */
let lastEmitted = model.value
/** 输入法组合期间收到的外部模型值,组合结束后再写入。 */
let pendingExternal: string | null = null

function syncEditable() { view?.dispatch({ effects: editable.reconfigure(EditorView.editable.of(!props.disabled)) }) }

function emitModel(): void {
  if (!view) return
  const text = view.state.doc.toString()
  lastEmitted = text
  if (model.value !== text) model.value = text
}

/** 只派发最小变更:整篇替换会让光标跳到末尾,并吞掉同一帧内继续输入的字符。 */
function applyExternal(value: string): void {
  if (!view) return
  const change = minimalTextChange(view.state.doc.toString(), value)
  if (!change) return
  external = true
  try { view.dispatch({ changes: change }) }
  catch { /* 编辑器正在销毁或处于非可编辑状态,忽略此次外部写入 */ }
  finally { external = false }
}

function settleAfterComposition(): void {
  const pending = pendingExternal
  pendingExternal = null
  if (pending !== null) { applyExternal(pending); return }
  emitModel()
}

function insert(text: string) {
  if (!view || props.disabled) return
  view.dispatch(view.state.replaceSelection(text))
  view.focus()
}
const actions = [
  { key: 'heading', icon: Heading2, text: '\n## ' },
  { key: 'list', icon: List, text: '\n- ' },
  { key: 'code', icon: Code, text: '\n```\n' },
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
        // 组合期间不把中间文本推给父级,避免父级重渲染打断候选词与光标。
        EditorView.updateListener.of((update) => {
          if (!update.docChanged || external || update.view.composing) return
          emitModel()
        }),
        EditorView.domEventHandlers({
          compositionend: () => {
            // CodeMirror 在组合结束这一轮事件之后才提交最终文本,延后一拍再对齐模型。
            setTimeout(settleAfterComposition, 0)
          },
          // 失焦时补一次同步,避免组合结束后立刻切换字段导致父级模型缺最后一段文本。
          blur: () => { setTimeout(settleAfterComposition, 0) },
        }),
      ],
    }),
  })
})
// 只有真正的外部变化(表单重置/草稿载入/格式切换)才写回编辑器。
watch(model, (value) => {
  if (!view || typeof value !== 'string') return
  if (!shouldApplyExternalModel(view.state.doc.toString(), value, lastEmitted)) return
  if (view.composing) { pendingExternal = value; return }
  pendingExternal = null
  applyExternal(value)
}, { flush: 'post' })
watch(() => props.disabled, syncEditable, { flush: 'post' })
onBeforeUnmount(() => { pendingExternal = null; view?.destroy(); view = null })
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
