<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { client } from '@/api/generated/client.gen'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'

interface Challenge {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  description?: string | null
  attachmentUrl?: string | null
}

interface SubmitResponse {
  correct: boolean
  message?: string | null
}

const props = defineProps<{
  open: boolean
  challenge: Challenge | null
  competitionId: string
  solved: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  solved: []
}>()

const { t } = useI18n()

const flagInput = ref('')
const submitting = ref(false)
const submitResult = ref<'correct' | 'incorrect' | null>(null)
const submitError = ref<string | null>(null)

const isOpen = computed({
  get: () => props.open,
  set: (v) => emit('update:open', v),
})

function onOpenChange(v: boolean) {
  if (!v) {
    flagInput.value = ''
    submitResult.value = null
    submitError.value = null
  }
  emit('update:open', v)
}

async function submitFlag() {
  if (!props.challenge || !flagInput.value.trim()) return
  submitting.value = true
  submitResult.value = null
  submitError.value = null

  try {
    const res = await client.post<{ 200: SubmitResponse }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/submit',
      path: { id: props.competitionId, challengeId: props.challenge.id },
      body: { flag: flagInput.value.trim() },
    })

    if (res.data?.correct) {
      submitResult.value = 'correct'
      emit('solved')
    } else {
      submitResult.value = 'incorrect'
    }
  } catch {
    submitError.value = t('challenges.submissionFailed')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <Dialog :open="isOpen" @update:open="onOpenChange">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <div class="flex items-center gap-2">
          <DialogTitle>{{ challenge?.title }}</DialogTitle>
          <Badge v-if="solved" class="bg-green-600 text-white border-transparent shrink-0">
            {{ t('challenges.solved') }}
          </Badge>
        </div>
        <div class="flex items-center gap-2 mt-1">
          <span class="text-xs text-muted-foreground">{{ challenge?.typeId }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs font-medium">{{ challenge?.points }} {{ $t('nav.score') }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs text-muted-foreground">{{ challenge?.solveCount }} {{ t('challenges.solves', { count: challenge?.solveCount ?? 0 }) }}</span>
        </div>
      </DialogHeader>

      <div class="space-y-4 py-2">
        <DialogDescription v-if="challenge?.description" class="text-sm text-foreground leading-relaxed">
          {{ challenge.description }}
        </DialogDescription>
        <p v-else class="text-sm text-muted-foreground italic">{{ t('challenges.noDescription') }}</p>

        <div v-if="challenge?.attachmentUrl" class="text-sm">
          <a
            :href="challenge.attachmentUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-primary underline-offset-4 hover:underline"
          >
            {{ t('challenges.downloadAttachment') }}
          </a>
        </div>

        <div v-if="!solved" class="space-y-2">
          <Label for="flag-input">{{ t('awd.flag') }}</Label>
          <div class="flex gap-2">
            <Input
              id="flag-input"
              v-model="flagInput"
              :placeholder="t('challenges.flagPlaceholder')"
              :disabled="submitting"
              @keydown.enter="submitFlag"
            />
            <Button :disabled="submitting || !flagInput.trim()" @click="submitFlag">
              {{ submitting ? t('common.submitting') : t('common.submit') }}
            </Button>
          </div>
        </div>

        <!-- Result feedback -->
        <div
          v-if="submitResult === 'correct'"
          class="rounded-md bg-green-500/10 border border-green-500/30 px-4 py-3 text-sm font-semibold text-green-700 dark:text-green-400"
        >
          ✓ {{ t('challenges.correctFlag') }}
        </div>
        <div
          v-else-if="submitResult === 'incorrect'"
          class="rounded-md bg-destructive/10 border border-destructive/30 px-4 py-3 text-sm font-semibold text-destructive"
        >
          ✗ {{ t('challenges.incorrectFlag') }}
        </div>
        <div
          v-else-if="submitError"
          class="rounded-md bg-destructive/10 border border-destructive/30 px-4 py-3 text-sm text-destructive"
        >
          {{ submitError }}
        </div>

        <div v-if="solved && submitResult !== 'correct'" class="text-sm text-muted-foreground italic">
          {{ t('challenges.alreadySolved') }}
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="onOpenChange(false)">{{ t('common.close') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
