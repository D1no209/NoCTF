<script setup lang="ts">
import type { GameModeValue, RuntimeTemplateModel } from '~/utils/game-config'
import {
  bytesToMib,
  coresToNanoCpus,
  emptyContainerDefinition,
  emptyComposeDefinition,
  FlagSource,
  mibToBytes,
  nanoCpusToCores,
  RuntimeAllocation,
  UrlExposure,
} from '~/utils/game-config'

const props = withDefaults(defineProps<{
  runtime: RuntimeTemplateModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

const isCompose = computed(() => props.runtime.definition.kind === 'compose')

const kindOptions = computed(() =>
  props.mode === 'Awdp'
    ? [{ value: 'container', label: translate("单容器") }]
    : [
        { value: 'container', label: translate("单容器") },
        { value: 'compose', label: 'Docker Compose' },
      ],
)

function switchKind(kind: string): void {
  if (kind === props.runtime.definition.kind) return
  props.runtime.definition = kind === 'compose'
    ? emptyComposeDefinition()
    : emptyContainerDefinition(props.mode === 'Ctf' || props.mode === 'Awdp')
}

const flagSourceOptions = computed(() => {
  const all = [
    { value: FlagSource.Static, label: translate("静态 Flag(模板预置)") },
    { value: FlagSource.PerTeam, label: translate("每队独立 Flag") },
    { value: FlagSource.AwdRotation, label: translate("按轮次轮换(AWD)") },
  ]
  return props.mode === 'Ctf' || props.mode === 'Awdp' ? all.slice(0, 2) : all
})

const exposureOptions = computed(() => {
  if (props.mode === 'Ctf' || props.mode === 'Awdp') {
    return [{ value: UrlExposure.OwnerOnly, label: translate("仅队伍自己可见") }]
  }
  return [
    { value: UrlExposure.OwnerOnly, label: translate("仅队伍自己可见") },
    { value: UrlExposure.Participants, label: translate("所有参赛者可见") },
  ]
})

/** KoH 控制检查入口:单条绑定,用 0/1 元素的数组适配 UrlBindingList。 */
const controlBindingList = computed({
  get: () => (props.runtime.controlCheckUrlBinding ? [props.runtime.controlCheckUrlBinding] : []),
  set: (list) => {
    props.runtime.controlCheckUrlBinding = list[0] ?? null
  },
})

// 切换游戏模式时,把运行时约束校正到目标模式(后端校验会拒绝不满足的组合)。
watch(
  () => props.mode,
  (mode) => {
    const runtime = props.runtime
    runtime.allocation = mode === 'Koh' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam
    if (mode !== 'Koh') runtime.controlCheckUrlBinding = null
    if (mode === 'Ctf' || mode === 'Awdp') {
      runtime.flagSource = FlagSource.PerTeam
      if (runtime.definition.kind === 'container' && !runtime.definition.flagEnvironmentVariableName.trim())
        runtime.definition.flagEnvironmentVariableName = 'FLAG'
      for (const binding of runtime.urlBindings) binding.exposure = UrlExposure.OwnerOnly
    }
    if (mode === 'Awdp') {
      if (runtime.definition.kind === 'compose') {
        runtime.definition = emptyContainerDefinition(true)
      }
    }
  },
  { immediate: true },
)
</script>

<template>
  <FieldGroup>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <FieldLabel>{{ $t('分配方式') }}</FieldLabel>
        <div class="flex h-9 items-center gap-2">
          <Badge variant="secondary">
            {{ runtime.allocation === RuntimeAllocation.Shared ? $t('共享') : $t('每队独立') }}
          </Badge>
        </div>
        <FieldDescription v-if="mode === 'Koh'">{{ $t('KoH 要求所有队伍共享同一套环境。') }}</FieldDescription>
        <FieldDescription v-else>{{ $t('{mode} 要求每个队伍独立的运行环境。', { mode }) }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('运行环境类型') }}</FieldLabel>
        <Select
          :model-value="runtime.definition.kind"
          :disabled="disabled || mode === 'Awdp'"
          @update:model-value="switchKind(String($event))"
        >
          <SelectTrigger class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <FieldDescription v-if="mode === 'Awdp'">{{ $t('AWDP 仅支持单容器运行环境。') }}</FieldDescription>
      </Field>
    </div>

    <DefinitionContainer
      v-if="runtime.definition.kind === 'container'"
      :definition="runtime.definition"
      :mode="mode"
      :flag-source="runtime.flagSource"
      :disabled="disabled"
    />
    <DefinitionCompose
      v-else
      :definition="runtime.definition"
      :disabled="disabled"
    />

    <DefinitionSection :title="$t('资源与生命周期')" :collapsible="false">
      <div class="grid gap-4 sm:grid-cols-3">
        <Field>
          <FieldLabel>{{ $t('内存(MiB)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="bytesToMib(runtime.limits.memoryBytes)"
            :min="1"
            placeholder="256"
            :disabled="disabled"
            @update:model-value="runtime.limits.memoryBytes = mibToBytes($event)"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('CPU(核)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="nanoCpusToCores(runtime.limits.nanoCpus)"
            :min="0"
            step="0.1"
            placeholder="0.5"
            :disabled="disabled"
            @update:model-value="runtime.limits.nanoCpus = coresToNanoCpus($event)"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('进程数上限') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.limits.pidsLimit"
            :min="1"
            placeholder="128"
            :disabled="disabled"
            @update:model-value="runtime.limits.pidsLimit = $event"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('实例存活时间(秒)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.ttlSeconds"
            :min="1"
            :max="604800"
            placeholder="3600"
            :disabled="disabled"
            @update:model-value="runtime.ttlSeconds = $event"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('操作超时(秒)') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.operationTimeoutSeconds"
            :min="1"
            :max="300"
            placeholder="60"
            :disabled="disabled"
            @update:model-value="runtime.operationTimeoutSeconds = $event"
          />
        </Field>
      </div>
      <FieldDescription>{{ $t('留空时使用平台默认资源限制与生命周期。') }}</FieldDescription>
    </DefinitionSection>

    <DefinitionSection :title="$t('Flag 与访问')" :collapsible="false">
      <Field v-if="mode !== 'Ctf' && mode !== 'Awdp'">
        <FieldLabel>{{ $t('Flag 来源') }}</FieldLabel>
        <Select
          :model-value="String(runtime.flagSource)"
          :disabled="disabled"
          @update:model-value="runtime.flagSource = Number($event)"
        >
          <SelectTrigger class="w-full sm:max-w-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in flagSourceOptions" :key="option.value" :value="String(option.value)">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field v-else>
        <FieldLabel>{{ $t('Flag 来源') }}</FieldLabel>
        <div class="flex h-9 items-center gap-2">
          <Badge variant="secondary">{{ $t('每队独立 Flag（环境变量注入）') }}</Badge>
        </div>
        <FieldDescription>{{ $t('容器题由平台生成队伍专属 Flag，并在启动环境时注入配置的环境变量。') }}</FieldDescription>
      </Field>

      <Field>
        <FieldLabel>{{ $t('访问入口') }}</FieldLabel>
        <UrlBindingList
          :model-value="runtime.urlBindings"
          :exposure-options="exposureOptions"
          :show-service-name="isCompose"
          :disabled="disabled"
          @update:model-value="runtime.urlBindings = $event"
        />
        <FieldDescription v-if="mode === 'Koh'"> {{ $t('KoH 开赛时要求至少一个「所有参赛者可见」的入口。') }} </FieldDescription>
        <FieldDescription v-else-if="mode === 'Awd'"> {{ $t('AWD 中选手互相访问对方服务,通常需要「所有参赛者可见」的入口。') }} </FieldDescription>
        <FieldDescription v-else-if="mode === 'Awdp'"> {{ $t('AWDP 攻击实例入口仅对所属队伍可见。') }} </FieldDescription>
      </Field>

      <Field v-if="mode === 'Koh'">
        <FieldLabel>{{ $t('控制检查入口') }}</FieldLabel>
        <UrlBindingList
          v-model="controlBindingList"
          :exposure-options="[{ value: UrlExposure.Participants, label: $t('平台检查使用') }]"
          :show-service-name="isCompose"
          :add-label="$t('设置控制检查入口')"
          :allow-custom-display="false"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('平台周期性检查控制权的地址;KoH 开赛必填。') }}</FieldDescription>
      </Field>
    </DefinitionSection>
  </FieldGroup>
</template>
