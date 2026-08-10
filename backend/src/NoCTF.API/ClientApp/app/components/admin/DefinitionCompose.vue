<script setup lang="ts">
import { Plus, X } from '@lucide/vue'
import type { ComposeDefinitionModel } from '~/utils/game-config'
import { bytesToMib, coresToNanoCpus, mibToBytes, nanoCpusToCores } from '~/utils/game-config'

withDefaults(defineProps<{
  definition: ComposeDefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})

function addService(definition: ComposeDefinitionModel): void {
  definition.serviceResources.push({ service: '', memoryBytes: null, nanoCpus: null, pidsLimit: null })
}
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>Compose YAML</FieldLabel>
      <Textarea
        v-model="definition.composeYaml"
        rows="12"
        class="font-mono text-xs"
        placeholder="services:&#10;  app:&#10;    image: ..."
        spellcheck="false"
        :disabled="disabled"
      />
      <FieldDescription> {{ $t('最多 64 个服务、1MB;不允许 privileged、挂载宿主机路径、网络模式等危险字段(平台会校验)。') }} </FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('服务资源限制') }}</FieldLabel>
      <div class="flex flex-col gap-2">
        <div
          v-for="(resource, index) in definition.serviceResources"
          :key="index"
          class="flex flex-wrap items-end gap-2 rounded-md border p-3"
        >
          <Field class="min-w-32 flex-1">
            <FieldLabel>{{ $t('服务名') }}</FieldLabel>
            <Input v-model="resource.service" placeholder="app" class="font-mono text-sm" :disabled="disabled" />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('内存(MiB)') }}</FieldLabel>
            <NullableNumberInput
              :model-value="bytesToMib(resource.memoryBytes)"
              :min="1"
              :disabled="disabled"
              @update:model-value="resource.memoryBytes = mibToBytes($event)"
            />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('CPU(核)') }}</FieldLabel>
            <NullableNumberInput
              :model-value="nanoCpusToCores(resource.nanoCpus)"
              :min="0"
              step="0.1"
              :disabled="disabled"
              @update:model-value="resource.nanoCpus = coresToNanoCpus($event)"
            />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('进程数上限') }}</FieldLabel>
            <NullableNumberInput
              :model-value="resource.pidsLimit"
              :min="1"
              :disabled="disabled"
              @update:model-value="resource.pidsLimit = $event"
            />
          </Field>
          <Button
            v-if="!disabled"
            type="button"
            variant="ghost"
            size="icon"
            class="shrink-0"
            @click="definition.serviceResources.splice(index, 1)"
          >
            <X class="size-4" />
          </Button>
        </div>
        <Button
          v-if="!disabled"
          type="button"
          variant="outline"
          size="sm"
          class="w-fit"
          @click="addService(definition)"
        >
          <Plus data-icon="inline-start" /> {{ $t('添加服务') }} </Button>
      </div>
      <FieldDescription>{{ $t('Compose 中每个服务都必须配置资源限制。') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('环境变量') }}</FieldLabel>
      <KeyValueEditor
        :model-value="definition.environment"
        :key-placeholder="$t('变量名')"
        :value-placeholder="$t('值')"
        :add-label="$t('添加环境变量')"
        :disabled="disabled"
        @update:model-value="definition.environment = $event"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t('标签') }}</FieldLabel>
      <KeyValueEditor
        :model-value="definition.labels"
        :key-placeholder="$t('标签名')"
        :value-placeholder="$t('值')"
        :add-label="$t('添加标签')"
        :disabled="disabled"
        @update:model-value="definition.labels = $event"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t('Flag 环境变量') }}</FieldLabel>
      <KeyValueEditor
        :model-value="definition.flagEnvironmentVariables"
        :key-placeholder="$t('服务名')"
        :value-placeholder="$t('环境变量名,如 FLAG')"
        :add-label="$t('添加注入目标')"
        :disabled="disabled"
        @update:model-value="definition.flagEnvironmentVariables = $event"
      />
      <FieldDescription>{{ $t('按队伍注入 Flag 时,每个服务对应的环境变量名。') }}</FieldDescription>
    </Field>
  </FieldGroup>
</template>
