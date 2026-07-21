<script setup lang="ts">
import { ArrowLeft, Puzzle } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandChallengeTemplateForm, {
  type CommandChallengeTemplateSubmit,
} from './CommandChallengeTemplateForm.vue'
import CommandPageHeader from './CommandPageHeader.vue'

const props = defineProps<{
  savePending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  back: []
  save: [value: CommandChallengeTemplateSubmit]
}>()
</script>

<template>
  <section class="admin-challenge-create">
    <CommandButton label="Back to challenges" tone="ghost" class="admin-challenge-create__back" @click="emit('back')">
      <template #icon><ArrowLeft class="size-4" /></template>
    </CommandButton>

    <CommandPageHeader
      signal-label="Admin API / challenge templates"
      signal-tone="warning"
      title="Create challenge template"
      description="Author a reusable challenge template with attachments, runtime containers, and checker configuration."
      :stat-icon="Puzzle"
      stat-value="NEW"
      stat-label="template"
    />

    <p v-if="props.operationMessage" class="admin-challenge-create__message" :class="`admin-challenge-create__message--${props.operationTone || 'danger'}`" role="alert">
      {{ props.operationMessage }}
    </p>

    <CommandChallengeTemplateForm
      :saving="props.savePending"
      submit-text="Create"
      cancel-text="Cancel"
      @submit="emit('save', $event)"
      @cancel="emit('back')"
    />
  </section>
</template>

<style scoped>
.admin-challenge-create { display: grid; align-content: start; gap: 16px; }
.admin-challenge-create__back { justify-self: start; }
.admin-challenge-create__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-challenge-create__message--danger { color: var(--v2-danger); }
</style>
