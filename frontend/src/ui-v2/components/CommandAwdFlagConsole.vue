<script setup lang="ts">
import { Send, ShieldAlert } from 'lucide-vue-next'
import type { CommandAwdChallenge } from './awd-contract'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = withDefaults(defineProps<{
  challenges: CommandAwdChallenge[]
  selectedChallengeId: string
  flag: string
  pending?: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>(), {
  pending: false,
  operationMessage: '',
  operationTone: 'success',
})

const emit = defineEmits<{
  'update:selectedChallengeId': [value: string]
  'update:flag': [value: string]
  submit: []
}>()
</script>

<template>
  <CommandPanel class="awd-flag-console" tone="signal">
    <header class="awd-flag-console__header">
      <div>
        <CommandSignal label="Flag endpoint" tone="info" />
        <h2>Submit flag</h2>
      </div>
      <ShieldAlert class="size-4 text-[var(--v2-primary)]" />
    </header>
    <div class="awd-flag-console__body">
      <p>This endpoint accepts a challenge identifier and flag value. Target selection is not part of the server contract.</p>
      <label>
        <span>Challenge</span>
        <CommandSelect
          :model-value="props.selectedChallengeId"
          label="Challenge"
          :options="[{ value: '', label: 'Select a challenge' }, ...props.challenges.map(challenge => ({ value: challenge.id, label: challenge.title }))]"
          @update:model-value="emit('update:selectedChallengeId', $event)"
        />
      </label>
      <label>
        <span>Flag</span>
        <CommandInput
          :model-value="props.flag"
          label="Flag"
          type="text"
          placeholder="flag{...}"
          @update:model-value="emit('update:flag', $event)"
        />
      </label>
      <CommandButton
        label="Submit"
        :disabled="props.pending || !props.selectedChallengeId || !props.flag.trim()"
        @click="emit('submit')"
      >
        <template #icon>
          <Send class="size-4" />
        </template>
      </CommandButton>
      <p v-if="props.operationMessage" class="awd-flag-console__message" :class="`awd-flag-console__message--${props.operationTone}`">
        {{ props.operationMessage }}
      </p>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awd-flag-console__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 16px 18px 8px; }
.awd-flag-console__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.awd-flag-console__body { display: grid; gap: 14px; padding: 8px 18px 18px; }
.awd-flag-console__body > p { margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.55; }
.awd-flag-console__body label { display: grid; gap: 7px; }
.awd-flag-console__body label > span { color: var(--v2-text-muted); font-size: 11px; font-weight: 600; letter-spacing: 0.04em; }
.awd-flag-console__body :deep(.command-button) { width: 100%; }
.awd-flag-console__message { margin: 0; border-radius: 12px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); color: var(--v2-cyan) !important; font-size: 12px; }
.awd-flag-console__message--danger { color: var(--v2-danger) !important; }
</style>
