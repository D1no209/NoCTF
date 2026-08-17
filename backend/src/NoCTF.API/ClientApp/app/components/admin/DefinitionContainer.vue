<script setup lang="ts">
import type { ContainerDefinitionModel, GameModeValue } from '~/utils/game-config'
import { FlagSource } from '~/utils/game-config'

const props = withDefaults(defineProps<{
  definition: ContainerDefinitionModel
  mode: GameModeValue
  /** 运行时的 Flag 来源;静态 Flag 时不能配置注入环境变量。 */
  flagSource?: number
  disabled?: boolean
}>(), {
  flagSource: FlagSource.Static,
  disabled: false,
})

const isAwdp = computed(() => props.mode === 'Awdp')
const flagEnvDisabled = computed(() => props.disabled || props.flagSource === FlagSource.Static)

// 存量内容非空时,对应高级分组默认展开,避免已配置的值被折叠藏住。
const hasMetadata = computed(() =>
  Object.keys(props.definition.environment).length > 0 || Object.keys(props.definition.labels).length > 0,
)
const hasSecurity = computed(() => {
  const security = props.definition.security
  return security.noNewPrivileges || security.readonlyRootfs || security.runAsNonRoot
    || security.capDrop.length > 0 || security.capAdd.length > 0
})
</script>

<template>
  <FieldGroup>
    <DefinitionSection :title="$t('基础')" :collapsible="false">
      <Field>
        <FieldLabel>{{ $t('镜像') }}</FieldLabel>
        <Input
          v-model="definition.image"
          placeholder="registry.example.com/challenge:latest"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('容器镜像地址,最长 512 字符。') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('启动命令') }}</FieldLabel>
        <StringListEditor
          :model-value="definition.command"
          :placeholder="$t('参数,如 --port')"
          :add-label="$t('添加参数')"
          :disabled="disabled"
          @update:model-value="definition.command = $event"
        />
        <FieldDescription>{{ $t('留空使用镜像默认入口;按参数逐项填写,不要做 shell 转义。') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('Flag 环境变量名') }}</FieldLabel>
        <Input
          v-model="definition.flagEnvironmentVariableName"
          placeholder="FLAG"
          class="font-mono text-sm sm:max-w-xs"
          :disabled="flagEnvDisabled"
        />
        <FieldDescription v-if="flagSource === FlagSource.Static"> {{ $t('仅「每队独立 Flag / 按轮次轮换」时可配置;静态 Flag 请在下方的 Flags 标签页维护。') }} </FieldDescription>
        <FieldDescription v-else>{{ $t('按队伍注入 Flag 时使用的环境变量名。') }}</FieldDescription>
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('网络')" :collapsible="false">
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('对外端口') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.containerPorts"
            :placeholder="$t('容器端口')"
            :add-label="$t('添加端口')"
            :disabled="disabled"
            @update:model-value="definition.containerPorts = $event"
          />
          <FieldDescription>{{ $t('主机端口由 Docker 随机分配,选手通过访问入口的 {PORT} 占位符访问。') }}</FieldDescription>
        </Field>
        <Field>
          <FieldLabel>{{ isAwdp ? $t('内部端口') : $t('内部端口(可选)') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.internalPorts"
            :placeholder="$t('仅平台内部可达的端口')"
            :add-label="$t('添加内部端口')"
            :disabled="disabled"
            @update:model-value="definition.internalPorts = $event"
          />
          <FieldDescription v-if="isAwdp">{{ $t('AWDP 要求恰好 1 个内部端口,checker 通过它检查服务。') }}</FieldDescription>
          <FieldDescription v-else>{{ $t('不对选手暴露、仅供平台内部访问的端口。') }}</FieldDescription>
        </Field>
      </div>
    </DefinitionSection>

    <DefinitionSection
      :title="$t('环境与元数据')"
      :hint="$t('环境变量与标签,大多数题目无需配置')"
      :default-open="hasMetadata"
    >
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
    </DefinitionSection>

    <DefinitionSection
      :title="$t('安全选项')"
      :hint="$t('容器加固与 Linux capabilities,通常保持默认')"
      :default-open="hasSecurity"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <Field orientation="horizontal">
          <Switch id="sec-no-new-privileges" v-model="definition.security.noNewPrivileges" :disabled="disabled" />
          <FieldLabel for="sec-no-new-privileges" class="font-normal">{{ $t('禁止提权(no-new-privileges)') }}</FieldLabel>
        </Field>
        <Field orientation="horizontal">
          <Switch id="sec-readonly-rootfs" v-model="definition.security.readonlyRootfs" :disabled="disabled" />
          <FieldLabel for="sec-readonly-rootfs" class="font-normal">{{ $t('只读根文件系统') }}</FieldLabel>
        </Field>
        <Field orientation="horizontal">
          <Switch id="sec-run-as-non-root" v-model="definition.security.runAsNonRoot" :disabled="disabled" />
          <FieldLabel for="sec-run-as-non-root" class="font-normal">{{ $t('以非 root 用户运行') }}</FieldLabel>
        </Field>
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('移除的能力(cap-drop)') }}</FieldLabel>
          <StringListEditor
            :model-value="definition.security.capDrop"
            :placeholder="$t('如 NET_RAW')"
            :add-label="$t('添加移除项')"
            :disabled="disabled"
            @update:model-value="definition.security.capDrop = $event"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('增加的能力(cap-add)') }}</FieldLabel>
          <StringListEditor
            :model-value="definition.security.capAdd"
            :placeholder="$t('如 SYS_PTRACE')"
            :add-label="$t('添加能力')"
            :disabled="disabled"
            @update:model-value="definition.security.capAdd = $event"
          />
        </Field>
      </div>
    </DefinitionSection>
  </FieldGroup>
</template>
