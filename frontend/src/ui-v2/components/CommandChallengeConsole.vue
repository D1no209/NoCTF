<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { CheckCircle2, XCircle } from 'lucide-vue-next'
import { competitionApi } from '@/api/noctf'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import type { CommandChallenge } from './CommandChallengeGrid.vue'

interface SubmitResponse {
  correct?: boolean
  alreadySolved?: boolean
  message?: string | null
  result?: string | null
}

const props = defineProps<{
  open: boolean
  competitionId: string
  challenge: CommandChallenge | null
  canSubmit: boolean
  submissionUnavailableReason?: string
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  submitted: []
}>()

const flag = ref('')
const submitting = ref(false)
const result = ref<'correct' | 'incorrect' | 'error' | null>(null)
const resultMessage = ref('')

const isOpen = computed({
  get: () => props.open,
  set: value => emit('update:open', value),
})

watch(() => [props.open, props.challenge?.id], () => {
  flag.value = ''
  result.value = null
  resultMessage.value = ''
})

async function submitFlag() {
  if (!props.challenge || !props.canSubmit || !flag.value.trim() || submitting.value)
    return

  submitting.value = true
  result.value = null
  resultMessage.value = ''
  try {
    const response = await competitionApi.submitFlag<SubmitResponse>(
      props.competitionId,
      props.challenge.id,
      flag.value.trim(),
    )
    if (response?.correct) {
      result.value = 'correct'
      resultMessage.value = response.alreadySolved ? 'This challenge was already solved by your team.' : 'Flag accepted.'
      emit('submitted')
      return
    }
    result.value = 'incorrect'
    resultMessage.value = response?.message || response?.result || 'Flag was not accepted.'
  }
  catch (error) {
    result.value = 'error'
    resultMessage.value = error instanceof Error ? error.message : 'Flag submission failed.'
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="isOpen && props.challenge" class="challenge-console" role="dialog" aria-modal="true" :aria-label="`${props.challenge.title} challenge console`">
      <button class="challenge-console__backdrop" aria-label="Close challenge console" @click="isOpen = false" />
      <CommandPanel class="challenge-console__dialog" tone="signal">
        <header>
          <div>
            <CommandSignal :label="props.challenge.typeId" tone="success" />
            <h2>{{ props.challenge.title }}</h2>
          </div>
          <CommandButton label="Close" tone="ghost" @click="isOpen = false" />
        </header>

        <div class="challenge-console__body">
          <p>{{ props.challenge.description || 'No challenge brief published.' }}</p>
          <dl>
            <div><dt>Score</dt><dd>{{ props.challenge.points }}</dd></div>
            <div><dt>Solves</dt><dd>{{ props.challenge.solveCount }}</dd></div>
          </dl>
          <div class="challenge-console__submit">
            <label for="command-flag">Flag submission</label>
            <CommandInput v-model="flag" type="text" label="Flag submission" placeholder="flag{...}" />
            <CommandButton :label="submitting ? 'Submitting' : 'Submit flag'" :disabled="!props.canSubmit || !flag.trim() || submitting" @click="submitFlag" />
          </div>
          <p v-if="!props.canSubmit && props.submissionUnavailableReason" class="challenge-console__notice">
            {{ props.submissionUnavailableReason }}
          </p>
          <div v-if="result" class="challenge-console__result" :class="`challenge-console__result--${result}`">
            <CheckCircle2 v-if="result === 'correct'" class="size-4" />
            <XCircle v-else class="size-4" />
            <span>{{ resultMessage }}</span>
          </div>
        </div>
      </CommandPanel>
    </div>
  </Teleport>
</template>

<style scoped>
.challenge-console { position: fixed; z-index: 50; inset: 0; display: grid; place-items: center; padding: 18px; }
.challenge-console__backdrop { position: absolute; inset: 0; border: 0; background: rgb(31 41 55 / 0.38); cursor: default; }
.challenge-console__dialog { position: relative; z-index: 1; width: min(620px, 100%); max-height: min(720px, calc(100dvh - 36px)); overflow-y: auto; }
.challenge-console__dialog > header { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; padding: 18px 18px 4px; }
.challenge-console__dialog h2 { margin: 8px 0 0; color: var(--v2-text); font-size: 19px; font-weight: 600; }
.challenge-console__body { display: grid; gap: 18px; padding: 14px 18px 20px; }
.challenge-console__body > p { margin: 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.65; white-space: pre-wrap; }
.challenge-console__body dl { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; margin: 0; }
.challenge-console__body dl div { border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.challenge-console__body dt { color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.03em; }
.challenge-console__body dd { margin: 6px 0 0; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 14px; font-weight: 600; }
.challenge-console__submit { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: end; gap: 10px; }
.challenge-console__submit label { grid-column: 1 / -1; color: var(--v2-text-muted); font-size: 11px; font-weight: 600; letter-spacing: 0.04em; }
.challenge-console__result { display: flex; align-items: flex-start; gap: 8px; border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; line-height: 1.45; }
.challenge-console__result--correct { color: var(--v2-cyan); }
.challenge-console__result--incorrect, .challenge-console__result--error { color: var(--v2-danger); }
.challenge-console__notice { margin: -8px 0 0; color: var(--v2-warning); font-size: 12px; line-height: 1.5; }

@media (max-width: 520px) {
  .challenge-console__submit { grid-template-columns: 1fr; }
  .challenge-console__submit :deep(.command-button) { width: 100%; }
}
</style>
