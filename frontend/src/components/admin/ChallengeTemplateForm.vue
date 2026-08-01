<script setup lang="ts">
import type { NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest } from '@/api/generated/types.gen'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Loader2 } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { challengeDirectionsForType, normalizeDirection } from '@/lib/challengeDirections'

const props = withDefaults(defineProps<{
  saving?: boolean
  submitText?: string
  cancelText?: string
}>(), {
  saving: false,
  submitText: '',
  cancelText: '',
})

const emit = defineEmits<{
  submit: [value: {
    payload: NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest
    attachmentFile: File | null
  }]
  cancel: []
}>()

const { t } = useI18n()
const modeOptions = ['Ctf', 'Awd', 'Awdp', 'Koh'] as const
const visibilityOptions = ['Private', 'Shared'] as const
const stableId = ref('')
const mode = ref<typeof modeOptions[number]>('Ctf')
const visibility = ref<typeof visibilityOptions[number]>('Private')
const title = ref('')
const description = ref('')
const direction = ref('WEB')
const definitionJson = ref('{}')
const attachmentFile = ref<File | null>(null)
const definitionError = ref('')

const directionOptions = computed(() => challengeDirectionsForType(mode.value))
const canSave = computed(() => Boolean(title.value.trim() && direction.value && definitionJson.value.trim()))

watch(mode, (nextMode) => {
  const directions = challengeDirectionsForType(nextMode)
  if (!directions.includes(normalizeDirection(direction.value)))
    direction.value = directions[0]
})

function onAttachmentChange(event: Event) {
  attachmentFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

function submit() {
  if (!canSave.value || props.saving)
    return

  try {
    JSON.parse(definitionJson.value)
    definitionError.value = ''
  }
  catch {
    definitionError.value = t('admin.challenges.invalidDefinitionJson')
    return
  }

  emit('submit', {
    payload: {
      id: stableId.value.trim() || undefined,
      mode: mode.value,
      visibility: visibility.value,
      title: title.value.trim(),
      description: description.value.trim() || undefined,
      direction: normalizeDirection(direction.value),
      definitionJson: definitionJson.value.trim(),
    },
    attachmentFile: attachmentFile.value,
  })
}
</script>

<template>
  <form class="grid gap-5" @submit.prevent="submit">
    <div class="grid gap-4 md:grid-cols-2">
      <div class="grid gap-2">
        <Label for="challenge-title">{{ t('admin.challenges.titleColumn') }}</Label>
        <Input id="challenge-title" v-model="title" required />
      </div>
      <div class="grid gap-2">
        <Label for="challenge-stable-id">{{ t('admin.challenges.stableId') }}</Label>
        <Input id="challenge-stable-id" v-model="stableId" :placeholder="t('admin.challenges.optional')" />
      </div>
    </div>

    <div class="grid gap-2">
      <Label for="challenge-description">{{ t('admin.challenges.description') }}</Label>
      <Textarea id="challenge-description" v-model="description" rows="4" />
    </div>

    <div class="grid gap-4 md:grid-cols-3">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.challengeMode') }}</Label>
        <Select v-model="mode">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in modeOptions" :key="option" :value="option">
              {{ option === 'Koh' ? 'KoH' : option.toUpperCase() }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.visibility') }}</Label>
        <Select v-model="visibility">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="Private">{{ t('admin.challenges.visibilityPrivate') }}</SelectItem>
            <SelectItem value="Shared">{{ t('admin.challenges.visibilityShared') }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.direction') }}</Label>
        <Select v-model="direction">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in directionOptions" :key="option" :value="option">
              {{ option }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>

    <div class="grid gap-2">
      <Label for="challenge-definition">{{ t('admin.challenges.definitionJson') }}</Label>
      <Textarea id="challenge-definition" v-model="definitionJson" class="min-h-56 font-mono text-xs" spellcheck="false" />
      <p class="text-xs text-muted-foreground">{{ t('admin.challenges.definitionJsonHint') }}</p>
      <p v-if="definitionError" class="text-sm text-destructive">{{ definitionError }}</p>
    </div>

    <div class="grid gap-2">
      <Label for="challenge-attachment">{{ t('admin.challenges.attachmentUpload') }}</Label>
      <Input id="challenge-attachment" type="file" @change="onAttachmentChange" />
    </div>

    <div class="flex justify-end gap-2">
      <Button type="button" variant="outline" :disabled="saving" @click="emit('cancel')">
        {{ cancelText || t('common.cancel') }}
      </Button>
      <Button type="submit" :disabled="saving || !canSave">
        <Loader2 v-if="saving" class="mr-2 size-4 animate-spin" />
        {{ submitText || t('common.create') }}
      </Button>
    </div>
  </form>
</template>
