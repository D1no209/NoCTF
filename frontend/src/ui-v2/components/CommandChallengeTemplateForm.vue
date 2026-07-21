<script setup lang="ts">
import { computed } from 'vue'
import { Box } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandFileInput from '../primitives/CommandFileInput.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandTextarea from '../primitives/CommandTextarea.vue'
import {
  challengeTypeOptions,
  useChallengeTemplateForm,
  type ChallengeTemplateInputDto,
  type ChallengeTemplateSubmit,
  type DeploymentTypeKey,
} from '@/features/admin/challengeTemplate'

export type CommandChallengeTemplate = ChallengeTemplateInputDto
export type CommandChallengeTemplateSubmit = ChallengeTemplateSubmit

const props = withDefaults(defineProps<{
  template?: CommandChallengeTemplate | null
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
  submit: [value: CommandChallengeTemplateSubmit]
  cancel: []
}>()

const deploymentTypeLabels: Record<DeploymentTypeKey, string> = {
  NoAttachment: 'No attachment',
  StaticAttachment: 'Static attachment',
  DynamicContainer: 'Dynamic container',
  StaticContainer: 'Static container',
}

const {
  form,
  attachmentFile,
  patchTemplateFile,
  selectedChallengeType,
  deploymentOptionKeys,
  usesRuntimeContainer,
  usesExpConfig,
  usesPatchTemplate,
  isContainerImageMissing,
  canSave,
  buildPayload,
} = useChallengeTemplateForm(() => props.template)

const deploymentOptions = computed(() => deploymentOptionKeys.value.map(key => ({ value: key, label: deploymentTypeLabels[key] })))
const checkerImageLabel = computed(() => selectedChallengeType.value === 'Awdp' ? 'AWDP check image' : 'Checker image')
const checkerCommandLabel = computed(() => selectedChallengeType.value === 'Awdp' ? 'AWDP check command' : 'Checker command')
const checkerTimeoutLabel = computed(() => selectedChallengeType.value === 'Awdp' ? 'AWDP check timeout (seconds)' : 'Checker timeout (seconds)')

function submit() {
  if (!canSave.value || props.saving)
    return
  emit('submit', { payload: buildPayload(), attachmentFile: attachmentFile.value, patchTemplateFile: patchTemplateFile.value })
}
</script>

<template>
  <form class="challenge-template-form" @submit.prevent="submit">
    <CommandInput v-model="form.title" label="Title" placeholder="Challenge title" />
    <CommandTextarea v-model="form.description" label="Description" :rows="4" placeholder="Challenge statement shown to players" />

    <div class="challenge-template-form__row">
      <CommandSelect v-model="form.typeId" label="Challenge mode" :options="challengeTypeOptions" />
      <CommandInput v-model="form.attachmentUrl" label="Attachment URL" placeholder="/api/files/challenge.zip" />
    </div>
    <div class="challenge-template-form__upload">
      <CommandFileInput
        label="Upload attachment"
        :file-name="attachmentFile?.name"
        @update:file="attachmentFile = $event"
      />
      <p class="challenge-template-form__hint">Leave empty to keep the current attachment.</p>
    </div>
    <div v-if="usesPatchTemplate" class="challenge-template-form__row">
      <CommandInput v-model="form.patchTemplateUrl" label="Patch template URL" placeholder="/api/files/patch-template.zip" />
      <CommandFileInput
        label="Upload patch template"
        accept=".zip,.tar.gz,.tgz"
        :file-name="patchTemplateFile?.name"
        @update:file="patchTemplateFile = $event"
      />
    </div>

    <div class="challenge-template-form__row">
      <CommandSelect v-model="form.deploymentType" label="Deployment type" :options="deploymentOptions" />
      <CommandInput
        v-if="usesRuntimeContainer"
        v-model="form.exposedPort"
        type="number"
        label="Exposed port"
        :min="1"
        :max="65535"
        placeholder="1 - 65535"
      />
    </div>

    <div class="challenge-template-form__row">
      <CommandInput
        v-model="form.flagSecret"
        label="Flag secret"
        :disabled="selectedChallengeType !== 'Ctf'"
        :placeholder="selectedChallengeType === 'Ctf' ? 'Static flag value' : 'Dynamic flags are managed by the competition'"
      />
      <CommandInput v-model="form.flagEnvironmentVariable" label="Flag environment variable" placeholder="NOCTF_FLAG_UUID" />
    </div>

    <CommandPanel v-if="usesRuntimeContainer" class="challenge-template-form__runtime">
      <div class="challenge-template-form__runtime-head">
        <Box class="size-4" />
        Runtime containers
      </div>
      <div class="challenge-template-form__row">
        <CommandSelect
          v-model="form.containerMode"
          label="Container mode"
          :options="[
            { value: 'SingleImage', label: 'Single image' },
            { value: 'DockerCompose', label: 'Docker compose' },
          ]"
        />
        <div class="challenge-template-form__image">
          <CommandInput
            v-model="form.containerImage"
            label="Challenge image (required)"
            placeholder="registry.example.com/challenge:latest"
          />
          <p v-if="isContainerImageMissing" class="challenge-template-form__hint challenge-template-form__hint--danger">
            A container image is required for runtime container deployments.
          </p>
        </div>
      </div>
      <CommandInput
        v-if="form.containerMode === 'DockerCompose'"
        v-model="form.composeProjectName"
        label="Compose project name"
        placeholder="challenge-stack"
      />
      <CommandTextarea
        v-if="form.containerMode === 'DockerCompose'"
        v-model="form.composeYaml"
        label="Compose YAML"
        :rows="7"
        mono
        placeholder="services: ..."
      />
      <div class="challenge-template-form__row">
        <CommandInput v-model="form.checkerImage" :label="checkerImageLabel" placeholder="Optional" />
        <CommandInput v-model="form.checkerCommand" :label="checkerCommandLabel" placeholder="Optional" />
      </div>
      <div class="challenge-template-form__row">
        <CommandInput v-model="form.checkerTimeoutSeconds" type="number" :label="checkerTimeoutLabel" :min="1" placeholder="Optional" />
        <CommandInput v-if="usesExpConfig" v-model="form.expImage" label="EXP image" placeholder="Optional" />
      </div>
      <CommandInput v-if="usesExpConfig" v-model="form.expCommand" label="EXP command" placeholder="Optional" />
    </CommandPanel>

    <div class="challenge-template-form__footer">
      <CommandButton type="button" tone="outline" :label="props.cancelText || 'Cancel'" :disabled="props.saving" @click="emit('cancel')" />
      <CommandButton
        type="submit"
        :label="props.saving ? 'Saving' : (props.submitText || 'Save')"
        :disabled="props.saving || !canSave"
      />
    </div>
  </form>
</template>

<style scoped>
.challenge-template-form { display: grid; gap: 14px; }
.challenge-template-form__row { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.challenge-template-form__upload { display: grid; gap: 6px; }
.challenge-template-form__hint { margin: 0; color: var(--v2-text-faint); font-size: 11px; }
.challenge-template-form__hint--danger { color: var(--v2-danger); }
.challenge-template-form__image { display: grid; align-content: start; gap: 6px; }
.challenge-template-form__runtime { display: grid; gap: 14px; padding: 16px; }
.challenge-template-form__runtime-head { display: flex; align-items: center; gap: 8px; color: var(--v2-text); font-size: 12px; font-weight: 600; letter-spacing: 0.08em; }
.challenge-template-form__footer { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 10px; }
</style>
