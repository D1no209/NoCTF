<script setup lang="ts">
import { toRefs } from 'vue'
import type { UrlBindingListViewState } from '~/features/admin/useUrlBindingList'

const viewProps = defineProps<{ state: UrlBindingListViewState }>()
const { Plus, X, displayTemplateOptions, update, remove, add, modelValue, exposureOptions, showServiceName, serviceNames, addLabel, allowCustomDisplay, disabled } = toRefs(viewProps.state)
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
            <FieldLabel>{{ allowCustomDisplay ? $t('ui.displayTemplate') : $t('ui.urlTemplate') }}</FieldLabel>
            <Select
              v-if="allowCustomDisplay"
              :model-value="binding.urlTemplate"
              :disabled="disabled"
              @update:model-value="update(index, { urlTemplate: String($event ?? '') })"
            >
              <SelectTrigger class="w-full font-mono text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent position="popper">
                <SelectGroup>
                  <SelectItem v-for="template in displayTemplateOptions" :key="template" :value="template" class="font-mono">
                    {{ template }}
                  </SelectItem>
                  <SelectItem
                    v-if="binding.urlTemplate && !displayTemplateOptions.includes(binding.urlTemplate)"
                    :value="binding.urlTemplate"
                    class="font-mono"
                  >
                    {{ binding.urlTemplate }}
                  </SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <Input
              v-else
              :model-value="binding.urlTemplate"
              :placeholder="$t('ui.httpHOSTPORT')"
              class="font-mono text-sm"
              :disabled="disabled"
              @update:model-value="update(index, { urlTemplate: String($event ?? '') })"
            />
          </Field>
          <Field>
            <FieldLabel>{{ $t('ui.exposureRange') }}</FieldLabel>
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
                    {{ $t(option.label) }}
                  </SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel>{{ $t('ui.containerPort') }}</FieldLabel>
            <NullableNumberInput
              :model-value="binding.containerPort"
              :min="1"
              :max="65535"
              :placeholder="$t('ui.containerPort')"
              :disabled="disabled"
              @update:model-value="update(index, { containerPort: $event })"
            />
          </Field>
          <Field v-if="showServiceName">
            <FieldLabel>{{ $t('ui.runtimeServiceName') }}</FieldLabel>
            <Select :model-value="binding.serviceName" :disabled="disabled"
              @update:model-value="update(index, { serviceName: String($event ?? '') })">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent><SelectGroup><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectGroup></SelectContent>
            </Select>
          </Field>
        </div>
        <Button
          v-if="!disabled"
          type="button"
          variant="ghost"
          size="icon"
          class="mt-6 shrink-0"
          :aria-label="$t('ui.removeItem', { index: index + 1 })"
          @click="remove(index)"
        >
          <X class="size-4" aria-hidden="true" />
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
      {{ $t(addLabel) }}
    </Button>
    <p v-if="!allowCustomDisplay" class="text-xs text-muted-foreground">
      {{ $t('ui.urlTemplatesOnlyAllowAndPlaceholdersIsARandomHost') }}
    </p>
  </div>
</template>
