<script setup lang="ts">
import type { DefinitionModel } from '~/utils/game-config'
import {
  bytesToMib,
  HARD_MAXIMUM_PATCH_UPLOAD_BYTES,
  mibToBytes,
} from '~/utils/game-config'

withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('补丁(Fix)') }}</FieldLegend>
    <FieldGroup>
      <Field>
        <FieldLabel>{{ $t('补丁入口') }}</FieldLabel>
        <Input
          v-model="model.patchEntrypoint"
          placeholder="patch.diff"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('选手提交的补丁存档中作为入口的文件路径。') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('补丁应用命令') }}</FieldLabel>
        <StringListEditor
          :model-value="model.patchCommand"
          :placeholder="$t('参数,如 -p1')"
          :add-label="$t('添加参数')"
          :disabled="disabled"
          @update:model-value="model.patchCommand = $event"
        />
        <FieldDescription>{{ $t('在重建的环境中应用补丁时执行的命令。') }}</FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('补丁超时(秒)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.patchTimeoutSeconds"
            :min="1"
            :disabled="disabled"
            @update:model-value="model.patchTimeoutSeconds = $event"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('就绪超时(秒)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.readyTimeoutSeconds"
            :min="1"
            :disabled="disabled"
            @update:model-value="model.readyTimeoutSeconds = $event"
          />
          <FieldDescription>{{ $t('补丁应用后等待服务就绪的时间。') }}</FieldDescription>
        </Field>
      </div>
      <Field>
        <FieldLabel>{{ $t('Fix 包上传上限(MiB)') }}</FieldLabel>
        <NullableNumberInput
          :model-value="bytesToMib(model.maximumPatchUploadBytes)"
          :min="1"
          :max="bytesToMib(HARD_MAXIMUM_PATCH_UPLOAD_BYTES) ?? undefined"
          :disabled="disabled"
          @update:model-value="model.maximumPatchUploadBytes = mibToBytes($event)"
        />
        <FieldDescription>
          {{ $t('限制选手上传的压缩包大小；默认 256 MiB，最大 1024 MiB。') }}
        </FieldDescription>
      </Field>
    </FieldGroup>
  </FieldSet>
</template>
