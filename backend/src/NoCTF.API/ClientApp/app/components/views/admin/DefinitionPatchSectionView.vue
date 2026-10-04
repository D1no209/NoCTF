<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionPatchSectionViewState } from '~/features/admin/useDefinitionPatchSection'

const viewProps = defineProps<{ state: DefinitionPatchSectionViewState }>()
const { bytesToMib, HARD_MAXIMUM_PATCH_UPLOAD_BYTES, mibToBytes, model, disabled, onUpdateModelValueModelPatchCommand, onUpdateModelValueModelPatchTimeoutSeconds, onUpdateModelValueModelReadyTimeoutSeconds, onUpdateModelValueModelMaximumPatchUploadBytes } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('administration.label.fix') }}</FieldLegend>
    <FieldGroup>
      <Field>
        <FieldLabel>{{ $t('administration.label.patchEntrance') }}</FieldLabel>
        <Input
          v-model="model.patchEntrypoint"
          :placeholder="$t('administration.label.patchDiff')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('administration.definitionPatch.description.filePathEntryPoint') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('administration.label.patchApplicationCommand') }}</FieldLabel>
        <StringListEditor
          :model-value="model.patchCommand"
          :placeholder="$t('administration.label.parametersP')"
          :add-label="$t('administration.label.addParameters')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueModelPatchCommand"
        />
        <FieldDescription>
          {{ $t('administration.definitionPatch.description.leaveBlankExecuteEntrypoint') }}
        </FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('administration.label.patchTimeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.patchTimeoutSeconds"
            :min="1"
            :max="300"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueModelPatchTimeoutSeconds"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('administration.label.readyTimeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.readyTimeoutSeconds"
            :min="1"
            :max="model.checkerJob?.timeoutSeconds ?? undefined"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueModelReadyTimeoutSeconds"
          />
          <FieldDescription>{{ $t('administration.definitionPatch.description.timeWaitServiceReady') }}</FieldDescription>
        </Field>
      </div>
      <Field>
        <FieldLabel>{{ $t('administration.definitionPatch.label.fixArchiveUploadLimit') }}</FieldLabel>
        <NullableNumberInput
          :model-value="bytesToMib(model.maximumPatchUploadBytes)"
          :min="1"
          :max="bytesToMib(HARD_MAXIMUM_PATCH_UPLOAD_BYTES) ?? undefined"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueModelMaximumPatchUploadBytes"
        />
        <FieldDescription>
          {{ $t('administration.definitionPatch.description.limitsParticipantFixArchives') }}
        </FieldDescription>
      </Field>
    </FieldGroup>
  </FieldSet>
</template>
