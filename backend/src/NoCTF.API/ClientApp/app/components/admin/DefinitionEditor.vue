<script setup lang="ts">
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'
import {
  FlagSource,
  emptyFlagTemplate,
  emptyRunnerJob,
  emptyRuntimeTemplate,
  parseDefinition,
  serializeDefinition,
} from '~/utils/game-config'

const props = withDefaults(defineProps<{
  /** definitionJson 字符串(v-model)。 */
  modelValue: string
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [json: string] }>()

const model = ref<DefinitionModel | null>(null)
const parseFailed = ref(false)
let lastSerialized = ''

watch(
  () => props.modelValue,
  (json) => {
    // 自身序列化回灌跳过重解析。
    if (json === lastSerialized) return
    const parsed = parseDefinition(json)
    if (parsed === null) {
      parseFailed.value = true
      model.value = null
      return
    }
    parseFailed.value = false
    model.value = parsed
    lastSerialized = json ?? ''
  },
  { immediate: true },
)

function emitSerialized(): void {
  if (!model.value) return
  const json = serializeDefinition(props.mode, model.value)
  lastSerialized = json
  emit('update:modelValue', json)
}

watch(model, emitSerialized, { deep: true })
watch(() => props.mode, emitSerialized)

function toggleRuntime(enabled: boolean): void {
  if (!model.value) return
  model.value.runtime = enabled ? emptyRuntimeTemplate(props.mode) : null
}

function toggleChecker(enabled: boolean): void {
  if (!model.value) return
  model.value.checker = enabled ? { job: emptyRunnerJob(), targetServiceName: '' } : null
}

function toggleCheckerJob(enabled: boolean): void {
  if (!model.value) return
  model.value.checkerJob = enabled ? emptyRunnerJob() : null
}

function toggleFlagInjection(enabled: boolean): void {
  if (!model.value) return
  model.value.flagInjection = enabled ? { command: '', timeoutSeconds: null, serviceName: '' } : null
}

function toggleFlagTemplate(enabled: boolean): void {
  if (!model.value) return
  model.value.flagTemplate = enabled ? emptyFlagTemplate() : null
}

const isCompose = computed(() => model.value?.runtime?.definition.kind === 'compose')
</script>

<template>
  <Alert v-if="parseFailed" variant="destructive">
    <AlertDescription>
      现有定义 JSON 无法解析,可能是历史遗留数据。请先在数据库或 API 层面修复后再编辑。
    </AlertDescription>
  </Alert>

  <FieldGroup v-else-if="model">
    <FieldSet class="rounded-md border p-4">
      <FieldLegend class="px-1 text-sm font-medium">运行环境</FieldLegend>
      <Field orientation="horizontal">
        <Switch
          id="def-has-runtime"
          :model-value="model.runtime !== null"
          :disabled="disabled"
          @update:model-value="toggleRuntime($event === true)"
        />
        <FieldLabel for="def-has-runtime" class="font-normal">选手需要在线运行环境(容器靶机)</FieldLabel>
      </Field>
      <DefinitionRuntime
        v-if="model.runtime"
        :runtime="model.runtime"
        :mode="mode"
        :disabled="disabled"
      />
      <FieldDescription v-else>
        纯静态题(如下载附件分析)不需要运行环境。
      </FieldDescription>
    </FieldSet>

    <template v-if="mode === 'Awd'">
      <FieldSet class="rounded-md border p-4">
        <FieldLegend class="px-1 text-sm font-medium">Flag 注入</FieldLegend>
        <Field orientation="horizontal">
          <Switch
            id="def-has-flag-injection"
            :model-value="model.flagInjection !== null"
            :disabled="disabled"
            @update:model-value="toggleFlagInjection($event === true)"
          />
          <FieldLabel for="def-has-flag-injection" class="font-normal">每轮向队伍环境注入新 Flag</FieldLabel>
        </Field>
        <FieldGroup v-if="model.flagInjection">
          <Field>
            <FieldLabel>注入命令</FieldLabel>
            <Input
              v-model="model.flagInjection.command"
              placeholder="sh -c 'echo ${FLAG} > /flag'"
              class="font-mono text-sm"
              :disabled="disabled"
            />
            <FieldDescription>必须包含 ${FLAG} 占位符,平台替换为本队本轮 Flag 后在容器内执行。</FieldDescription>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel>超时(秒)</FieldLabel>
              <NullableNumberInput
                :model-value="model.flagInjection.timeoutSeconds"
                :min="1"
                placeholder="默认 30"
                :disabled="disabled"
                @update:model-value="model.flagInjection!.timeoutSeconds = $event"
              />
            </Field>
            <Field v-if="isCompose">
              <FieldLabel>目标服务名</FieldLabel>
              <Input
                v-model="model.flagInjection.serviceName"
                placeholder="compose 中的 service 名"
                class="font-mono text-sm"
                :disabled="disabled"
              />
              <FieldDescription>Compose 运行环境必须指定注入目标服务。</FieldDescription>
            </Field>
          </div>
        </FieldGroup>
        <FieldDescription v-else>启用运行环境时,AWD 必须配置 Flag 注入。</FieldDescription>
      </FieldSet>

      <FieldSet class="rounded-md border p-4">
        <FieldLegend class="px-1 text-sm font-medium">Checker(服务健康检查)</FieldLegend>
        <Field orientation="horizontal">
          <Switch
            id="def-has-checker"
            :model-value="model.checker !== null"
            :disabled="disabled"
            @update:model-value="toggleChecker($event === true)"
          />
          <FieldLabel for="def-has-checker" class="font-normal">启用周期性服务检查</FieldLabel>
        </Field>
        <template v-if="model.checker">
          <RunnerJobEditor :job="model.checker.job" :disabled="disabled" />
          <Field v-if="isCompose">
            <FieldLabel>目标服务名</FieldLabel>
            <Input
              v-model="model.checker.targetServiceName"
              placeholder="compose 中的 service 名"
              class="font-mono text-sm"
              :disabled="disabled"
            />
            <FieldDescription>Compose 运行环境必须指定被检查的服务;单容器时留空。</FieldDescription>
          </Field>
        </template>
      </FieldSet>

      <FieldSet class="rounded-md border p-4">
        <FieldLegend class="px-1 text-sm font-medium">Flag 模板</FieldLegend>
        <Field orientation="horizontal">
          <Switch
            id="def-has-flag-template"
            :model-value="model.flagTemplate !== null"
            :disabled="disabled"
            @update:model-value="toggleFlagTemplate($event === true)"
          />
          <FieldLabel for="def-has-flag-template" class="font-normal">自定义每队 Flag 生成模板</FieldLabel>
        </Field>
        <FlagTemplateEditor v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
        <FieldDescription v-else>未配置时使用竞赛级 Flag 模板或平台默认。</FieldDescription>
      </FieldSet>
    </template>

    <FieldSet
      v-if="mode === 'Ctf' && model.runtime?.flagSource === FlagSource.PerTeam"
      class="rounded-md border p-4"
    >
      <FieldLegend class="px-1 text-sm font-medium">动态 Flag 模板覆盖</FieldLegend>
      <Field orientation="horizontal">
        <Switch
          id="def-has-ctf-flag-template"
          :model-value="model.flagTemplate !== null"
          :disabled="disabled"
          @update:model-value="toggleFlagTemplate($event === true)"
        />
        <FieldLabel for="def-has-ctf-flag-template" class="font-normal">
          为本题覆盖竞赛级动态 Flag 模板
        </FieldLabel>
      </Field>
      <FlagTemplateEditor v-if="model.flagTemplate" :template="model.flagTemplate" :disabled="disabled" />
      <FieldDescription v-else>
        未配置时使用竞赛级模板；只影响今后生成的每队容器 Flag。
      </FieldDescription>
    </FieldSet>

    <template v-if="mode === 'Awdp'">
      <FieldSet class="rounded-md border p-4">
        <FieldLegend class="px-1 text-sm font-medium">补丁(Fix)</FieldLegend>
        <FieldGroup>
          <Field>
            <FieldLabel>补丁入口</FieldLabel>
            <Input
              v-model="model.patchEntrypoint"
              placeholder="patch.diff"
              class="font-mono text-sm"
              :disabled="disabled"
            />
            <FieldDescription>选手提交的补丁存档中作为入口的文件路径。</FieldDescription>
          </Field>
          <Field>
            <FieldLabel>补丁应用命令</FieldLabel>
            <StringListEditor
              :model-value="model.patchCommand"
              placeholder="参数,如 -p1"
              add-label="添加参数"
              :disabled="disabled"
              @update:model-value="model!.patchCommand = $event"
            />
            <FieldDescription>在重建的环境中应用补丁时执行的命令。</FieldDescription>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel>补丁超时(秒)</FieldLabel>
              <NullableNumberInput
                :model-value="model.patchTimeoutSeconds"
                :min="1"
                :disabled="disabled"
                @update:model-value="model!.patchTimeoutSeconds = $event"
              />
            </Field>
            <Field>
              <FieldLabel>就绪超时(秒)</FieldLabel>
              <NullableNumberInput
                :model-value="model.readyTimeoutSeconds"
                :min="1"
                :disabled="disabled"
                @update:model-value="model!.readyTimeoutSeconds = $event"
              />
              <FieldDescription>补丁应用后等待服务就绪的时间。</FieldDescription>
            </Field>
          </div>
        </FieldGroup>
      </FieldSet>

      <FieldSet class="rounded-md border p-4">
        <FieldLegend class="px-1 text-sm font-medium">Checker(服务健康检查)</FieldLegend>
        <Field orientation="horizontal">
          <Switch
            id="def-has-checker-job"
            :model-value="model.checkerJob !== null"
            :disabled="disabled"
            @update:model-value="toggleCheckerJob($event === true)"
          />
          <FieldLabel for="def-has-checker-job" class="font-normal">启用周期性服务检查</FieldLabel>
        </Field>
        <RunnerJobEditor v-if="model.checkerJob" :job="model.checkerJob" :disabled="disabled" />
      </FieldSet>
    </template>

    <FieldDescription>
      定义修改对未来启动/重置的实例生效,已存在的运行实例不受影响。
    </FieldDescription>
  </FieldGroup>
</template>
