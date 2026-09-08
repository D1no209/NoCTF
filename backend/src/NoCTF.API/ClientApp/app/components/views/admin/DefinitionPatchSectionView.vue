<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionPatchSectionViewState } from '~/features/admin/useDefinitionPatchSection'

const viewProps = defineProps<{ state: DefinitionPatchSectionViewState }>()
const { bytesToMib, HARD_MAXIMUM_PATCH_UPLOAD_BYTES, mibToBytes, model, disabled, onUpdateModelValueModelPatchCommand, onUpdateModelValueModelPatchTimeoutSeconds, onUpdateModelValueModelReadyTimeoutSeconds, onUpdateModelValueModelMaximumPatchUploadBytes } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('ui.fix') }}</FieldLegend>
    <FieldGroup>
      <Field>
        <FieldLabel>{{ $t('ui.patchEntrance') }}</FieldLabel>
        <Input
          v-model="model.patchEntrypoint"
          :placeholder="$t('ui.patchDiff')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('ui.theFilePathUsedAsTheEntryPointInThe') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.patchApplicationCommand') }}</FieldLabel>
        <StringListEditor
          :model-value="model.patchCommand"
          :placeholder="$t('ui.parametersSuchAsP1')"
          :add-label="$t('ui.addParameters')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueModelPatchCommand"
        />
        <FieldDescription>
          {{ $t('ui.leaveBlankToExecuteTheEntrypointFileACustomCommand') }}
        </FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('ui.patchTimeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.patchTimeoutSeconds"
            :min="1"
            :max="300"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueModelPatchTimeoutSeconds"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.readyTimeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.readyTimeoutSeconds"
            :min="1"
            :max="model.checkerJob?.timeoutSeconds ?? undefined"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueModelReadyTimeoutSeconds"
          />
          <FieldDescription>{{ $t('ui.theTimeToWaitForTheServiceToBeReady') }}</FieldDescription>
        </Field>
      </div>
      <Field>
        <FieldLabel>{{ $t('ui.fixArchiveUploadLimitMib') }}</FieldLabel>
        <NullableNumberInput
          :model-value="bytesToMib(model.maximumPatchUploadBytes)"
          :min="1"
          :max="bytesToMib(HARD_MAXIMUM_PATCH_UPLOAD_BYTES) ?? undefined"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueModelMaximumPatchUploadBytes"
        />
        <FieldDescription>
          {{ $t('ui.limitsParticipantFixArchivesTheDefaultIs256MibAnd') }}
        </FieldDescription>
      </Field>
    </FieldGroup>
  </FieldSet>
</template>
