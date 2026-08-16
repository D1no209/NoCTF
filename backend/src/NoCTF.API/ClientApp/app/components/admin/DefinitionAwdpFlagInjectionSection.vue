<script setup lang="ts">
import { AwdpFlagInjectionKind, type DefinitionModel } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})

function toggle(enabled: boolean): void {
  props.model.awdpFlagInjection = enabled
    ? { kind: AwdpFlagInjectionKind.EnvironmentVariable, environmentVariableName: 'FLAG', filePath: '' }
    : null
}

function setKind(value: unknown): void {
  if (!props.model.awdpFlagInjection) return
  const kind = Number(value)
  if (kind !== AwdpFlagInjectionKind.EnvironmentVariable && kind !== AwdpFlagInjectionKind.File) return
  props.model.awdpFlagInjection.kind = kind
  if (kind === AwdpFlagInjectionKind.EnvironmentVariable) props.model.awdpFlagInjection.filePath = ''
  else props.model.awdpFlagInjection.environmentVariableName = ''
}
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('AWDP 动态 Flag 注入') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-awdp-flag-injection"
        :model-value="model.awdpFlagInjection !== null"
        :disabled="disabled"
        @update:model-value="toggle($event === true)"
      />
      <FieldLabel for="def-has-awdp-flag-injection" class="font-normal">{{ $t('为每支队伍的攻击实例注入独立 Flag') }}</FieldLabel>
    </Field>
    <FieldGroup v-if="model.awdpFlagInjection">
      <Field>
        <FieldLabel>{{ $t('注入方式') }}</FieldLabel>
        <Select
          :model-value="String(model.awdpFlagInjection.kind)"
          :disabled="disabled"
          @update:model-value="setKind"
        >
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem :value="String(AwdpFlagInjectionKind.EnvironmentVariable)">{{ $t('环境变量') }}</SelectItem>
            <SelectItem :value="String(AwdpFlagInjectionKind.File)">{{ $t('受保护文件') }}</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field v-if="model.awdpFlagInjection.kind === AwdpFlagInjectionKind.EnvironmentVariable">
        <FieldLabel>{{ $t('环境变量名') }}</FieldLabel>
        <Input
          v-model="model.awdpFlagInjection.environmentVariableName"
          placeholder="FLAG"
          class="font-mono text-sm"
          :disabled="disabled"
        />
      </Field>
      <Field v-else>
        <FieldLabel>{{ $t('容器内文件路径') }}</FieldLabel>
        <Input
          v-model="model.awdpFlagInjection.filePath"
          placeholder="/run/noctf/flag"
          class="font-mono text-sm"
          :disabled="disabled"
        />
      </Field>
    </FieldGroup>
    <FieldDescription>{{ $t('这里只配置注入位置；Flag 前缀和正文模板在本场比赛的题目规则中配置。') }}</FieldDescription>
  </FieldSet>
</template>
