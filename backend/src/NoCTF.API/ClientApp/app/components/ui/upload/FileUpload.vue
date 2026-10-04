<script setup lang="ts">import type { UiMessage } from '../../../utils/i18n'

import { Upload, X } from '@lucide/vue'
import { computed, ref } from 'vue'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ accept?: string; multiple?: boolean; disabled?: boolean; id?: string; required?: boolean; pending?: boolean; progress?: number | null; error?: UiMessage | null }>()
const emit = defineEmits<{ change: [event: Event] }>()
const input = ref<HTMLInputElement | null>(null)
const files = ref<File[]>([])
const dragging = ref(false)
const blocked = computed(() => props.disabled || props.pending)
const percentage = computed(() => props.progress == null ? undefined : Math.min(100, Math.max(0, props.progress)))
function choose() { if (!blocked.value) input.value?.click() }
function changed(event: Event) {
  files.value = Array.from((event.target as HTMLInputElement).files ?? [])
  emit('change', event)
}
function drop(event: DragEvent) {
  dragging.value = false
  if (blocked.value || !input.value || !event.dataTransfer?.files.length) return
  const transfer = new DataTransfer()
  const selected = Array.from(event.dataTransfer.files)
  for (const file of props.multiple ? selected : selected.slice(0, 1)) transfer.items.add(file)
  input.value.files = transfer.files
  input.value.dispatchEvent(new Event('change', { bubbles: true }))
}
function remove(index: number) {
  if (!input.value) return
  const transfer = new DataTransfer()
  files.value.filter((_, i) => i !== index).forEach(file => transfer.items.add(file))
  input.value.files = transfer.files
  input.value.dispatchEvent(new Event('change', { bubbles: true }))
}
defineExpose({ click: choose })
</script>

<template>
  <div data-slot="file-upload" v-bind="$attrs" :data-dragging="dragging || undefined" :data-disabled="disabled || undefined" @dragover.prevent="dragging = !disabled" @dragleave="dragging = false" @drop.prevent="drop">
    <input ref="input" type="file" class="hidden" tabindex="-1" aria-hidden="true" :accept="accept" :multiple="multiple" :disabled="blocked" :required="required" :aria-label="$t('upload.choose')" @change="changed">
    <Button :id="id" type="button" variant="outline" :disabled="blocked" @click="choose"><Upload data-icon="inline-start" />{{ $t('upload.choose') }}</Button>
    <p class="text-sm text-muted-foreground">{{ $t('upload.drop') }}</p>
    <ul v-if="files.length" class="flex flex-col gap-2">
      <li v-for="(file, index) in files" :key="`${file.name}-${index}`" class="flex items-center gap-2 border-t pt-2">
        <span class="min-w-0 flex-1 break-all text-sm">{{ file.name }} <span class="text-muted-foreground">{{ $t('upload.size', { size: Math.ceil(file.size / 1024) }) }}</span></span>
        <Button type="button" variant="ghost" size="icon-sm" :disabled="blocked" :aria-label="$t('upload.remove', { name: file.name })" @click="remove(index)"><X /></Button>
      </li>
    </ul>
    <div v-if="pending || percentage !== undefined" role="progressbar" :aria-label="$t('upload.pending')" :aria-valuenow="percentage" :aria-valuemin="0" :aria-valuemax="100">
      <div v-if="percentage !== undefined" class="h-1 overflow-hidden bg-muted"><div class="h-full bg-primary" :style="{ width: `${percentage}%` }" /></div>
      <p v-else class="flex items-center gap-2 text-sm text-muted-foreground"><Spinner />{{ $t('upload.pending') }}</p>
    </div>
    <FieldError v-if="error" role="alert">{{ $message(error) }}</FieldError>
  </div>
</template>
