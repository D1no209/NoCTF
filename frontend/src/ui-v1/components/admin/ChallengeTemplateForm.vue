<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Box, Loader2 } from 'lucide-vue-next'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Textarea } from '@/ui-v1/components/ui/textarea'
import { Label } from '@/ui-v1/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'
import {
  challengeTypeOptions,
  useChallengeTemplateForm,
  type ChallengeTemplateInputDto,
  type ChallengeTemplateSubmit,
} from '@/features/admin/challengeTemplate'

const props = withDefaults(defineProps<{
  template?: ChallengeTemplateInputDto | null
  saving?: boolean
  submitText?: string
  cancelText?: string
}>(), {
  template: null,
  saving: false,
  submitText: '',
  cancelText: '',
})

const emit = defineEmits<{
  submit: [value: ChallengeTemplateSubmit]
  cancel: []
}>()

const { t } = useI18n()

const {
  form,
  attachmentFile,
  patchTemplateFile,
  selectedChallengeType,
  directionOptions,
  deploymentOptionKeys: deploymentOptions,
  usesRuntimeContainer,
  usesExpConfig,
  usesPatchTemplate,
  isContainerImageMissing,
  canSave,
  buildPayload,
} = useChallengeTemplateForm(() => props.template)

const checkerImageLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckImage')
  : t('admin.challenges.checkerImage'))
const checkerCommandLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckCommand')
  : t('admin.challenges.checkerCommand'))
const checkerTimeoutLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckTimeout')
  : t('admin.challenges.checkerTimeout'))

function submit() {
  if (!canSave.value || props.saving) return
  emit('submit', { payload: buildPayload(), attachmentFile: attachmentFile.value, patchTemplateFile: patchTemplateFile.value })
}
</script>

<template>
  <form class="grid gap-5" @submit.prevent="submit">
    <div class="grid gap-2">
      <Label>{{ t('admin.challenges.titleColumn') }}</Label>
      <Input v-model="form.title" />
    </div>
    <div class="grid gap-2">
      <Label>{{ t('admin.challenges.description') }}</Label>
      <Textarea v-model="form.description" rows="4" />
    </div>

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.challengeMode') }}</Label>
        <Select v-model="form.typeId">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in challengeTypeOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.direction') }}</Label>
        <Select v-model="form.direction">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="direction in directionOptions" :key="direction" :value="direction">
              {{ direction }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.attachmentUrl') }}</Label>
        <Input v-model="form.attachmentUrl" placeholder="/api/files/challenge.zip" />
      </div>
      <div class="grid gap-2 sm:col-span-2">
        <Label>{{ t('admin.challenges.attachmentUpload') }}</Label>
        <Input type="file" @change="attachmentFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
        <p class="text-xs text-muted-foreground">{{ t('admin.challenges.attachmentEmptyHint') }}</p>
      </div>
      <div v-if="usesPatchTemplate" class="grid gap-2">
        <Label>{{ t('admin.challenges.patchTemplateUrl') }}</Label>
        <Input v-model="form.patchTemplateUrl" placeholder="/api/files/patch-template.zip" />
      </div>
      <div v-if="usesPatchTemplate" class="grid gap-2">
        <Label>{{ t('admin.challenges.patchTemplateUpload') }}</Label>
        <Input type="file" accept=".zip,.tar.gz,.tgz" @change="patchTemplateFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
        <p class="text-xs text-muted-foreground">{{ t('admin.challenges.patchTemplateHint') }}</p>
      </div>
    </div>

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.deploymentType') }}</Label>
        <Select v-model="form.deploymentType">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="deployment in deploymentOptions" :key="deployment" :value="deployment">
              {{ t(`admin.challenges.deploymentTypes.${deployment}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div v-if="usesRuntimeContainer" class="grid gap-2">
        <Label>{{ t('admin.challenges.exposedPort') }}</Label>
        <Input v-model.number="form.exposedPort" type="number" min="1" max="65535" :placeholder="t('admin.challenges.exposedPortPlaceholder')" />
      </div>
    </div>

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.flagSecret') }}</Label>
        <Input
          v-model="form.flagSecret"
          :disabled="selectedChallengeType !== 'Ctf'"
          :placeholder="selectedChallengeType === 'Ctf' ? t('admin.challenges.flagSecretPlaceholder') : t('admin.challenges.dynamicFlagManagedByCompetition')"
        />
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.flagEnvironmentVariable') }}</Label>
        <Input v-model="form.flagEnvironmentVariable" placeholder="NOCTF_FLAG_UUID" />
      </div>
    </div>

    <Card v-if="usesRuntimeContainer" class="p-4">
      <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-wider">
        <Box class="size-4" />
        {{ t('admin.challenges.runtimeContainers') }}
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label>{{ t('admin.challenges.challengeContainerMode') }}</Label>
          <Select v-model="form.containerMode">
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="SingleImage">{{ t('admin.challenges.singleImage') }}</SelectItem>
              <SelectItem value="DockerCompose">{{ t('admin.challenges.dockerCompose') }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="grid gap-2">
          <Label>
            {{ t('admin.challenges.challengeImage') }}
            <span class="text-destructive">*</span>
          </Label>
          <Input v-model="form.containerImage" required :aria-invalid="isContainerImageMissing" />
          <p v-if="isContainerImageMissing" class="text-xs text-destructive">
            {{ t('admin.challenges.challengeImageRequired') }}
          </p>
        </div>
        <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
          <Label>{{ t('admin.challenges.composeProjectName') }}</Label>
          <Input v-model="form.composeProjectName" />
        </div>
      </div>
      <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
        <Label>{{ t('admin.challenges.composeYaml') }}</Label>
        <Textarea v-model="form.composeYaml" class="font-mono text-xs" rows="7" />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label>{{ checkerImageLabel }}</Label>
          <Input v-model="form.checkerImage" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div class="grid gap-2">
          <Label>{{ checkerCommandLabel }}</Label>
          <Input v-model="form.checkerCommand" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div class="grid gap-2">
          <Label>{{ checkerTimeoutLabel }}</Label>
          <Input v-model.number="form.checkerTimeoutSeconds" type="number" min="1" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div v-if="usesExpConfig" class="grid gap-2">
          <Label>{{ t('admin.challenges.expImage') }}</Label>
          <Input v-model="form.expImage" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div v-if="usesExpConfig" class="grid gap-2 sm:col-span-2">
          <Label>{{ t('admin.challenges.expCommand') }}</Label>
          <Input v-model="form.expCommand" :placeholder="t('admin.challenges.optional')" />
        </div>
      </div>
    </Card>

    <div class="flex flex-col-reverse gap-2 border-t pt-5 sm:flex-row sm:justify-end">
      <Button type="button" variant="outline" :disabled="saving" @click="emit('cancel')">
        {{ cancelText || t('common.cancel') }}
      </Button>
      <Button type="submit" :disabled="saving || !canSave">
        <Loader2 v-if="saving" class="mr-2 size-4 animate-spin" />
        {{ submitText || t('common.save') }}
      </Button>
    </div>
  </form>
</template>
