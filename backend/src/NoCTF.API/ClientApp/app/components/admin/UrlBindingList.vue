<script setup lang="ts">
import { Plus, X } from '@lucide/vue'
import type { UrlBindingModel } from '~/utils/game-config'
import { UrlExposure } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** Compose 运行时需要选择服务名。 */
  showServiceName?: boolean
  addLabel?: string
  disabled?: boolean
}>(), {
  exposureOptions: () => [
    { value: UrlExposure.OwnerOnly, label: '仅队伍自己可见' },
    { value: UrlExposure.Participants, label: '所有参赛者可见' },
  ],
  showServiceName: false,
  addLabel: '添加访问入口',
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: UrlBindingModel[]] }>()

function update(index: number, patch: Partial<UrlBindingModel>): void {
  const next = props.modelValue.map((binding, i) => (i === index ? { ...binding, ...patch } : binding))
  emit('update:modelValue', next)
}

function remove(index: number): void {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}

function add(): void {
  emit('update:modelValue', [
    ...props.modelValue,
    {
      urlTemplate: 'http://{HOST}:{PORT}',
      exposure: props.exposureOptions[0]?.value ?? UrlExposure.Participants,
      containerPort: null,
      serviceName: '',
    },
  ])
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <div
      v-for="(binding, index) in modelValue"
      :key="index"
      class="flex flex-col gap-2 rounded-md border p-3"
    >
      <div class="flex items-start gap-2">
        <div class="grid flex-1 gap-2 sm:grid-cols-2">
          <Field>
            <FieldLabel>URL 模板</FieldLabel>
            <Input
              :model-value="binding.urlTemplate"
              placeholder="http://{HOST}:{PORT}/"
              class="font-mono text-sm"
              :disabled="disabled"
              @update:model-value="update(index, { urlTemplate: String($event ?? '') })"
            />
          </Field>
          <Field>
            <FieldLabel>暴露范围</FieldLabel>
            <Select
              :model-value="String(binding.exposure)"
              :disabled="disabled || exposureOptions.length <= 1"
              @update:model-value="update(index, { exposure: Number($event) })"
            >
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="option in exposureOptions" :key="option.value" :value="String(option.value)">
                    {{ option.label }}
                  </SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel>容器端口</FieldLabel>
            <NullableNumberInput
              :model-value="binding.containerPort"
              :min="1"
              :max="65535"
              placeholder="从端口映射中解析"
              :disabled="disabled"
              @update:model-value="update(index, { containerPort: $event })"
            />
          </Field>
          <Field v-if="showServiceName">
            <FieldLabel>服务名(Compose)</FieldLabel>
            <Input
              :model-value="binding.serviceName"
              placeholder="compose 中的 service 名"
              class="font-mono text-sm"
              :disabled="disabled"
              @update:model-value="update(index, { serviceName: String($event ?? '') })"
            />
          </Field>
        </div>
        <Button
          v-if="!disabled"
          type="button"
          variant="ghost"
          size="icon"
          class="mt-6 shrink-0"
          @click="remove(index)"
        >
          <X class="size-4" />
        </Button>
      </div>
    </div>
    <Button
      v-if="!disabled"
      type="button"
      variant="outline"
      size="sm"
      class="w-fit"
      @click="add"
    >
      <Plus data-icon="inline-start" />
      {{ addLabel }}
    </Button>
    <p class="text-xs text-muted-foreground">
      URL 模板只允许 {HOST} 与 {PORT} 占位符;{PORT} 为平台分配的随机主机端口。
    </p>
  </div>
</template>
