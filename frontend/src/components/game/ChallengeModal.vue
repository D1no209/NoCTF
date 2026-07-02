<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Alert } from '@/components/ui/alert'
import { renderMarkdown } from '@/lib/markdown'
import { toast } from 'vue-sonner'
import { CheckCircle2, Download, Loader2, Shield, Server, Upload } from 'lucide-vue-next'

interface Challenge {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  description?: string | null
  descriptionFormat?: string | null
  hints?: string[]
  attachmentUrl?: string | null
}

interface SubmitResponse {
  correct: boolean
  message?: string | null
}

interface PatchSubmissionStatus {
  id?: string
  challengeId: string
  status: string | number
  submittedAt?: string
  validatedAt?: string | null
  validationDetail?: string | null
}

const props = defineProps<{
  open: boolean
  challenge: Challenge | null
  competitionId: string
  solved: boolean
  gameModeType?: string
  isAwdMode?: boolean
  isAwdpMode?: boolean
  instanceReady?: boolean
  defenseEnabled?: boolean
  patchSubmissions?: PatchSubmissionStatus[]
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'create-instance': []
  'request-defense': []
  'patch-uploaded': []
  solved: []
}>()

const { t } = useI18n()

const flagInput = ref('')
const submitting = ref(false)
const submitResult = ref<'correct' | 'incorrect' | null>(null)
const submitError = ref<string | null>(null)
const patchFile = ref<File | null>(null)
const patchUploading = ref(false)
const instanceCreating = ref(false)
const instanceStatus = ref<string | null>(null)

const isOpen = computed({
  get: () => props.open,
  set: (v) => emit('update:open', v),
})

const renderedDescription = computed(() => {
  if (!props.challenge?.description) return ''
  return renderMarkdown(props.challenge.description)
})

const visibleHints = computed(() => props.challenge?.hints?.filter(Boolean) ?? [])
const patchStatuses = computed(() => props.patchSubmissions ?? [])
const canUploadPatch = computed(() => Boolean(props.isAwdMode && props.defenseEnabled && patchFile.value && props.challenge))

function onOpenChange(v: boolean) {
  if (!v) {
    flagInput.value = ''
    submitResult.value = null
    submitError.value = null
    patchFile.value = null
    instanceStatus.value = null
  }
  emit('update:open', v)
}

async function createInstance() {
  if (!props.challenge) return
  instanceCreating.value = true
  instanceStatus.value = null
  try {
    const data = await competitionApi.createInstance<{ ports?: Record<string, number>; status?: string }>(
      props.competitionId,
      props.challenge.id,
    )
    const ports = data.ports ? Object.entries(data.ports).map(([container, host]) => `${container}->${host}`).join(', ') : ''
    instanceStatus.value = ports ? `${data.status ?? 'running'} ${ports}` : data.status ?? 'running'
    toast.success(t('challenges.instanceReady'))
    emit('create-instance')
  } catch {
    toast.error(t('challenges.instanceFailed'))
  } finally {
    instanceCreating.value = false
  }
}

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
}

async function submitFlag() {
  if (!props.challenge || !flagInput.value.trim()) return
  submitting.value = true
  submitResult.value = null
  submitError.value = null

  try {
    const data = await competitionApi.submitFlag<SubmitResponse>(
      props.competitionId,
      props.challenge.id,
      flagInput.value.trim(),
    )

    if (data?.correct) {
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

async function submitPatch() {
  if (!props.challenge || !patchFile.value || !props.defenseEnabled) return
  patchUploading.value = true

  try {
    await competitionApi.submitPatch(props.competitionId, props.challenge.id, patchFile.value)
    toast.success(t('awd.patchSubmitted'))
    patchFile.value = null
    emit('patch-uploaded')
  } catch {
    toast.error(t('awd.patchUploadFailed'))
  } finally {
    patchUploading.value = false
  }
}

function formatDate(value?: string | null) {
  if (!value) return ''
  return new Date(value).toLocaleString()
}
</script>

<template>
  <Dialog :open="isOpen" @update:open="onOpenChange">
    <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
      <DialogHeader>
        <div class="flex items-center gap-2">
          <DialogTitle>{{ challenge?.title }}</DialogTitle>
          <Badge v-if="solved" class="bg-green-600 text-white border-transparent shrink-0">
            {{ t('challenges.solved') }}
          </Badge>
        </div>
        <div class="mt-1 flex items-center gap-2">
          <span class="text-xs text-muted-foreground">{{ challenge?.typeId }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs font-medium">{{ challenge?.points }} {{ $t('nav.score') }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs text-muted-foreground">{{ challenge?.solveCount }} {{ t('challenges.solves', { count: challenge?.solveCount ?? 0 }) }}</span>
        </div>
      </DialogHeader>

      <div class="space-y-4 py-2">
        <div
          v-if="renderedDescription"
          class="challenge-markdown text-sm text-foreground"
          v-html="renderedDescription"
        />
        <p v-else class="text-sm text-muted-foreground italic">{{ t('challenges.noDescription') }}</p>

        <div class="grid gap-3 rounded-lg border bg-muted/25 p-3 sm:grid-cols-2">
          <Button variant="outline" class="justify-start" :disabled="instanceReady || instanceCreating" @click="createInstance">
            <Loader2 v-if="instanceCreating" class="mr-2 size-4 animate-spin" />
            <Server v-else class="mr-2 size-4" />
            {{ instanceReady ? t('challenges.instanceReady') : t('challenges.createInstance') }}
          </Button>
          <Button
            v-if="isAwdMode"
            variant="outline"
            class="justify-start"
            :disabled="defenseEnabled"
            @click="emit('request-defense')"
          >
            <Shield class="mr-2 size-4" />
            {{ defenseEnabled ? t('challenges.defenseReady') : t('challenges.requestDefense') }}
          </Button>
        </div>
        <div v-if="instanceStatus" class="rounded-md border bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
          {{ instanceStatus }}
        </div>

        <div v-if="visibleHints.length" class="rounded-md border bg-muted/30 p-3">
          <div class="mb-2 text-xs font-medium uppercase text-muted-foreground">{{ t('challenges.hints') }}</div>
          <ol class="space-y-1 pl-4 text-sm leading-relaxed list-decimal">
            <li v-for="(hint, index) in visibleHints" :key="`${index}-${hint}`">
              {{ hint }}
            </li>
          </ol>
        </div>

        <div v-if="challenge?.attachmentUrl" class="text-sm">
          <a
            :href="challenge.attachmentUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-primary underline-offset-4 hover:underline"
          >
            <Download class="mr-1 inline size-4" />
            {{ t('challenges.downloadAttachment') }}
          </a>
        </div>

        <div v-if="isAwdMode" class="space-y-3 rounded-lg border bg-muted/20 p-3">
          <div class="flex items-center justify-between gap-3">
            <div>
              <div class="text-sm font-semibold">{{ t('awd.uploadPatch') }}</div>
              <p class="text-xs text-muted-foreground">
                {{ defenseEnabled ? t('challenges.patchUnlocked') : t('challenges.patchLocked') }}
              </p>
            </div>
            <Badge :variant="defenseEnabled ? 'default' : 'outline'">
              {{ defenseEnabled ? t('challenges.defenseReady') : t('challenges.defenseRequired') }}
            </Badge>
          </div>
          <div class="flex flex-col gap-2 sm:flex-row">
            <Input type="file" accept=".tar.gz,.tgz" :disabled="!defenseEnabled || patchUploading" @change="onPatchFileChange" />
            <Button class="shrink-0" :disabled="patchUploading || !canUploadPatch" @click="submitPatch">
              <Loader2 v-if="patchUploading" class="mr-2 size-4 animate-spin" />
              <Upload v-else class="mr-2 size-4" />
              {{ t('awd.submitPatch') }}
            </Button>
          </div>
        </div>

        <div v-if="isAwdpMode" class="space-y-2 rounded-lg border bg-card p-3">
          <div class="text-sm font-semibold">{{ t('awd.patchStatus') }}</div>
          <div v-if="patchStatuses.length" class="divide-y">
            <div v-for="patch in patchStatuses" :key="patch.id ?? `${patch.challengeId}-${patch.submittedAt}`" class="space-y-1 py-2">
              <div class="flex items-center justify-between gap-3">
                <span class="text-xs text-muted-foreground">{{ formatDate(patch.submittedAt) }}</span>
                <Badge variant="secondary">{{ patch.status }}</Badge>
              </div>
              <p v-if="patch.validationDetail" class="text-xs leading-relaxed text-muted-foreground">
                <CheckCircle2 class="mr-1 inline size-3" />
                {{ patch.validationDetail }}
              </p>
            </div>
          </div>
          <p v-else class="text-xs text-muted-foreground">{{ t('challenges.noPatchRecords') }}</p>
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
        <Alert v-if="submitResult === 'correct'" variant="success">
          {{ t('challenges.correctFlag') }}
        </Alert>
        <Alert v-else-if="submitResult === 'incorrect'" variant="destructive">
          {{ t('challenges.incorrectFlag') }}
        </Alert>
        <Alert v-else-if="submitError" variant="destructive">
          {{ submitError }}
        </Alert>

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

<style scoped>
.challenge-markdown :deep(p) {
  margin: 0 0 0.75rem;
  line-height: 1.65;
}

.challenge-markdown :deep(p:last-child) {
  margin-bottom: 0;
}

.challenge-markdown :deep(h3),
.challenge-markdown :deep(h4),
.challenge-markdown :deep(h5) {
  margin: 0.9rem 0 0.4rem;
  font-weight: 650;
  line-height: 1.35;
}

.challenge-markdown :deep(ul) {
  margin: 0.5rem 0 0.75rem;
  padding-left: 1.25rem;
  list-style: disc;
}

.challenge-markdown :deep(li) {
  margin: 0.25rem 0;
}

.challenge-markdown :deep(code) {
  border-radius: 0.25rem;
  background: hsl(var(--muted));
  padding: 0.1rem 0.3rem;
  font-size: 0.85em;
}

.challenge-markdown :deep(a) {
  color: hsl(var(--primary));
  text-underline-offset: 0.2rem;
}

.challenge-markdown :deep(a:hover) {
  text-decoration: underline;
}
</style>
