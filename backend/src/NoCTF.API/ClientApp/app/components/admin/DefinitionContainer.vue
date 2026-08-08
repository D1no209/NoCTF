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
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>镜像</FieldLabel>
      <Input
        v-model="definition.image"
        placeholder="registry.example.com/challenge:latest"
        class="font-mono text-sm"
        :disabled="disabled"
      />
      <FieldDescription>容器镜像地址,最长 512 字符。</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>启动命令</FieldLabel>
      <StringListEditor
        :model-value="definition.command"
        placeholder="参数,如 --port"
        add-label="添加参数"
        :disabled="disabled"
        @update:model-value="definition.command = $event"
      />
      <FieldDescription>留空使用镜像默认入口;按参数逐项填写,不要做 shell 转义。</FieldDescription>
    </Field>
    <Field v-if="!isAwdp">
      <FieldLabel>对外端口</FieldLabel>
      <NumberListEditor
        :model-value="definition.containerPorts"
        placeholder="容器端口"
        add-label="添加端口"
        :disabled="disabled"
        @update:model-value="definition.containerPorts = $event"
      />
      <FieldDescription>主机端口由 Docker 随机分配,选手通过访问入口的 {PORT} 占位符访问。</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ isAwdp ? '内部端口' : '内部端口(可选)' }}</FieldLabel>
      <NumberListEditor
        :model-value="definition.internalPorts"
        placeholder="仅平台内部可达的端口"
        add-label="添加内部端口"
        :disabled="disabled"
        @update:model-value="definition.internalPorts = $event"
      />
      <FieldDescription v-if="isAwdp">AWDP 要求恰好 1 个内部端口,checker 通过它检查服务。</FieldDescription>
      <FieldDescription v-else>不对选手暴露、仅供平台内部访问的端口。</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>环境变量</FieldLabel>
      <KeyValueEditor
        :model-value="definition.environment"
        key-placeholder="变量名"
        value-placeholder="值"
        add-label="添加环境变量"
        :disabled="disabled"
        @update:model-value="definition.environment = $event"
      />
    </Field>
    <Field>
      <FieldLabel>标签</FieldLabel>
      <KeyValueEditor
        :model-value="definition.labels"
        key-placeholder="标签名"
        value-placeholder="值"
        add-label="添加标签"
        :disabled="disabled"
        @update:model-value="definition.labels = $event"
      />
    </Field>
    <Field>
      <FieldLabel>Flag 环境变量名</FieldLabel>
      <Input
        v-model="definition.flagEnvironmentVariableName"
        placeholder="FLAG"
        class="font-mono text-sm"
        :disabled="flagEnvDisabled"
      />
      <FieldDescription v-if="flagSource === FlagSource.Static">
        仅「每队独立 Flag / 按轮次轮换」时可配置;静态 Flag 请在下方的 Flags 标签页维护。
      </FieldDescription>
      <FieldDescription v-else>按队伍注入 Flag 时使用的环境变量名。</FieldDescription>
    </Field>
    <FieldSet class="rounded-md border p-3">
      <FieldLegend class="text-sm font-medium">安全选项</FieldLegend>
      <Field orientation="horizontal">
        <Switch id="sec-no-new-privileges" v-model="definition.security.noNewPrivileges" :disabled="disabled" />
        <FieldLabel for="sec-no-new-privileges" class="font-normal">禁止提权(no-new-privileges)</FieldLabel>
      </Field>
      <Field orientation="horizontal">
        <Switch id="sec-readonly-rootfs" v-model="definition.security.readonlyRootfs" :disabled="disabled" />
        <FieldLabel for="sec-readonly-rootfs" class="font-normal">只读根文件系统</FieldLabel>
      </Field>
      <Field orientation="horizontal">
        <Switch id="sec-run-as-non-root" v-model="definition.security.runAsNonRoot" :disabled="disabled" />
        <FieldLabel for="sec-run-as-non-root" class="font-normal">以非 root 用户运行</FieldLabel>
      </Field>
      <Field>
        <FieldLabel>移除的能力(cap-drop)</FieldLabel>
        <StringListEditor
          :model-value="definition.security.capDrop"
          placeholder="如 NET_RAW"
          add-label="添加移除项"
          :disabled="disabled"
          @update:model-value="definition.security.capDrop = $event"
        />
      </Field>
      <Field>
        <FieldLabel>增加的能力(cap-add)</FieldLabel>
        <StringListEditor
          :model-value="definition.security.capAdd"
          placeholder="如 SYS_PTRACE"
          add-label="添加能力"
          :disabled="disabled"
          @update:model-value="definition.security.capAdd = $event"
        />
      </Field>
    </FieldSet>
  </FieldGroup>
</template>
