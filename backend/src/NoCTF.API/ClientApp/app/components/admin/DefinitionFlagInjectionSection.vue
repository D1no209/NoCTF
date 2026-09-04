<script setup lang="ts">
import type { DefinitionModel } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})

const isCompose = computed(() => props.model.runtime?.definition.kind === 'compose')

function toggleFlagInjection(enabled: boolean): void {
  props.model.flagInjection = enabled ? { command: '', timeoutSeconds: null, serviceName: '' } : null
}
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('Flag 注入') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-flag-injection"
        :model-value="model.flagInjection !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagInjection($event === true)"
      />
      <FieldLabel for="def-has-flag-injection" class="font-normal">{{ $t('每轮向队伍环境注入新 Flag') }}</FieldLabel>
    </Field>
    <FieldGroup v-if="model.flagInjection">
      <Field>
        <FieldLabel>{{ $t('注入命令') }}</FieldLabel>
        <Input
          v-model="model.flagInjection.command"
          placeholder="sh -c 'echo ${FLAG} > /flag'"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('必须包含 ${FLAG} 占位符,平台替换为本队本轮 Flag 后在容器内执行。') }}</FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('超时(秒)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.flagInjection.timeoutSeconds"
            :min="1"
            :max="300"
            :placeholder="$t('默认 30')"
            :disabled="disabled"
            @update:model-value="model.flagInjection!.timeoutSeconds = $event"
          />
        </Field>
        <Field v-if="isCompose">
          <FieldLabel>{{ $t('目标服务名') }}</FieldLabel>
          <Input
            v-model="model.flagInjection.serviceName"
            :placeholder="$t('compose 中的 service 名')"
            class="font-mono text-sm"
            :disabled="disabled"
          />
          <FieldDescription>{{ $t('Compose 运行环境必须指定注入目标服务。') }}</FieldDescription>
        </Field>
      </div>
    </FieldGroup>
    <FieldDescription v-else>{{ $t('启用运行环境时,AWD 必须配置 Flag 注入。') }}</FieldDescription>
  </FieldSet>
</template>
