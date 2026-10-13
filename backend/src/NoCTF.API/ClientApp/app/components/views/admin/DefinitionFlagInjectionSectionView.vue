<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionFlagInjectionSectionViewState } from '~/features/admin/useDefinitionFlagInjectionSection'

const viewProps = defineProps<{ state: DefinitionFlagInjectionSectionViewState }>()
const { model, disabled, serviceNames, canAddStore, addStore, removeStore, setDeliveryMode, setTimeoutSeconds, setCommand, useFileTemplate } = toRefs(viewProps.state)
</script>

<template>
  <DefinitionSection :title="$t('administration.awd.stores')" :collapsible="false">
    <FieldDescription>{{ $t('administration.awd.storeBudgetHint') }}</FieldDescription>
    <FieldDescription v-if="model.flagInjection">{{ $t('administration.awd.legacyStoreHint') }}</FieldDescription>
    <Empty v-if="!model.awdFlagStores.length"><EmptyTitle>{{ $t('administration.awd.noStores') }}</EmptyTitle></Empty>
    <div v-for="(store, index) in model.awdFlagStores" :key="store.id" class="flex flex-col gap-4 py-3">
      <Separator v-if="index" />
      <div class="flex items-end gap-3">
        <Field class="flex-1">
          <FieldLabel :for="`awd-store-name-${store.id}`">{{ $t('administration.awd.storeName') }}</FieldLabel>
          <Input :id="`awd-store-name-${store.id}`" v-model="store.name" :maxlength="64" :disabled="disabled" />
        </Field>
        <Button v-if="!disabled && model.awdFlagStores.length > 1" variant="ghost" @click="removeStore(index)">{{ $t('administration.awd.removeStore') }}</Button>
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('administration.awd.deliveryMode') }}</FieldLabel>
          <Select :model-value="store.deliveryMode" :disabled="disabled" @update:model-value="setDeliveryMode(index, $event)">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem value="RunnerCommand">{{ $t('administration.awd.runnerCommand') }}</SelectItem>
              <SelectItem value="BusinessChecker">{{ $t('administration.awd.businessChecker') }}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <Field v-if="serviceNames.length">
          <FieldLabel>{{ $t('administration.label.targetServiceName') }}</FieldLabel>
          <Select v-model="store.targetServiceName" :disabled="disabled">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
      </div>
      <template v-if="store.deliveryMode === 'RunnerCommand'">
        <Field>
          <FieldLabel>{{ $t('administration.label.injectCommand') }}</FieldLabel>
          <Textarea :model-value="store.injectionCommand ?? ''" @update:model-value="setCommand(index, $event)" :rows="3" :maxlength="8192" :disabled="disabled" class="font-mono" />
          <FieldDescription>{{ $t('administration.awd.commandHint') }}</FieldDescription>
        </Field>
        <Button v-if="!disabled" class="w-fit" variant="outline" @click="useFileTemplate(index)">{{ $t('administration.awd.fileTemplate') }}</Button>
        <Field>
          <FieldLabel>{{ $t('administration.label.timeoutSeconds') }}</FieldLabel>
          <NullableNumberInput :model-value="store.injectionTimeoutSeconds ?? null" :min="1" :max="300" :disabled="disabled" @update:model-value="setTimeoutSeconds(index, $event)" />
        </Field>
      </template>
      <FieldDescription v-else>{{ $t('administration.awd.checkerDeliveryHint') }}</FieldDescription>
      <Field orientation="horizontal">
        <Switch :id="`awd-history-${store.id}`" v-model="store.preservesHistoricalFlags" :disabled="disabled" />
        <FieldLabel :for="`awd-history-${store.id}`">{{ $t('administration.awd.preserveHistory') }}</FieldLabel>
      </Field>
    </div>
    <Button v-if="!disabled" class="w-fit" variant="outline" :disabled="!canAddStore" @click="addStore">{{ $t('administration.awd.addStore') }}</Button>
  </DefinitionSection>
</template>
