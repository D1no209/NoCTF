<script setup lang="ts">
import { Globe, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformGetPublicGateway, adminPlatformGetPublicGatewayStatus, adminPlatformUpdatePublicGateway } from '~/api'
import type { NoCtfapiEndpointsAdministrationPlatformPublicGatewayConfigurationResponse as Configuration, NoCtfapiEndpointsAdministrationPlatformPublicGatewayStatusResponse as GatewayStatus } from '~/api'
import { gatewayHost, gatewayOrigin, publicGatewayFailure, publicGatewayState } from '~/utils/public-gateway'

definePageMeta({ middleware: 'platform-admin' })
const configuration = ref<Configuration | null>(null)
const status = ref<GatewayStatus | null>(null)
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const statusError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string>>({})
const saved = ref('')
const form = reactive({ enabled: false, connectorId: '', publicOrigin: '', directOrigins: '', publicRuntimeHost: '', directRuntimeHostOverride: '', maxPublishedPorts: 8 })
const dirty = computed(() => JSON.stringify(form) !== saved.value)
const capability = computed(() => configuration.value?.capability)
let generation = 0
let statusTimer: ReturnType<typeof setInterval> | undefined
let readingStatus = false

async function readStatus(): Promise<boolean> {
  if (readingStatus) return false
  readingStatus = true
  try {
    const result = await adminPlatformGetPublicGatewayStatus()
    if (result.error || !result.data) throw result.error
    status.value = result.data
    statusError.value = null
    return result.data.applied === true
  }
  catch (failure) {
    statusError.value = parseApiError(failure).message
    return false
  }
  finally { readingStatus = false }
}
const application = usePolling(readStatus, { interval: 1000, maxInterval: 3000, timeout: 30_000 })
async function load() {
  const current = ++generation
  loading.value = true
  error.value = null
  try {
    const result = await adminPlatformGetPublicGateway()
    if (current !== generation) return
    if (result.error || !result.data) throw result.error
    configuration.value = result.data
    const policy = result.data.policy
    Object.assign(form, { enabled: policy?.enabled ?? false, connectorId: policy?.connectorId || result.data.capability?.connectorId || '',
      publicOrigin: policy?.publicOrigin || result.data.capability?.approvedOrigins?.[0] || '',
      directOrigins: (policy?.directOrigins ?? []).join('\n'), publicRuntimeHost: policy?.publicRuntimeHost ?? '',
      directRuntimeHostOverride: policy?.directRuntimeHostOverride ?? '', maxPublishedPorts: policy?.maxPublishedPorts || result.data.capability?.maximumPorts || 8 })
    saved.value = JSON.stringify(form)
    await readStatus()
  }
  catch (failure) { error.value = parseApiError(failure).message }
  finally { if (current === generation) loading.value = false }
}
async function save() {
  if (saving.value) return
  const directOrigins = form.directOrigins.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
  const issues: Record<string, string> = {}
  const origin = gatewayOrigin(form.publicOrigin, true)
  if (!origin || !capability.value?.approvedOrigins?.some(value => gatewayOrigin(value, true) === origin))
    issues.publicOrigin = translate('请选择部署已批准的 HTTPS 公网入口')
  if (!directOrigins.length || directOrigins.length > 16 || directOrigins.some(value => !gatewayOrigin(value))
    || new Set(directOrigins.map(value => gatewayOrigin(value))).size !== directOrigins.length
    || directOrigins.some(value => gatewayOrigin(value) === origin))
    issues.directOrigins = translate('填写 1 至 16 个不重复的内网入口，每行一个，不得包含公网入口')
  if (!gatewayHost(form.publicRuntimeHost)) issues.publicRuntimeHost = translate('填写主机名或 IPv4 地址，不包含协议、路径或端口')
  if (form.directRuntimeHostOverride && !gatewayHost(form.directRuntimeHostOverride))
    issues.directRuntimeHostOverride = translate('填写主机名或 IPv4 地址，不包含协议、路径或端口')
  if (!Number.isInteger(form.maxPublishedPorts) || form.maxPublishedPorts < 1 || form.maxPublishedPorts > (capability.value?.maximumPorts ?? 0))
    issues.maxPublishedPorts = translate('公开端口数量必须在部署允许的配额内')
  fieldErrors.value = issues
  if (Object.keys(issues).length) return
  saving.value = true
  error.value = null
  try {
    const result = await adminPlatformUpdatePublicGateway({ body: { ...form, directOrigins, directRuntimeHostOverride: form.directRuntimeHostOverride || null } })
    if (result.error || !result.data) throw result.error
    configuration.value = result.data.configuration ?? configuration.value
    saved.value = JSON.stringify(form)
    toast.success(translate('网关设置已保存，正在应用'))
    application.start()
  }
  catch (failure) { error.value = parseApiError(failure).message }
  finally { saving.value = false }
}
onMounted(() => {
  void load()
  statusTimer = setInterval(() => {
    if (!application.polling.value && (configuration.value?.policy?.enabled || status.value?.runtimes?.length)) void readStatus()
  }, 5000)
})
onBeforeUnmount(() => { generation++; if (statusTimer) clearInterval(statusTimer) })
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex flex-col gap-1">
        <h2 class="text-lg font-semibold">{{ $t('内网穿透') }}</h2>
        <p class="max-w-2xl text-sm text-muted-foreground">{{ $t('公网连接独立于题目运行；关闭公网不会停止题目或固定网站隧道。') }}</p>
      </div>
      <Button variant="outline" :disabled="loading || application.polling.value" @click="application.start()"><RefreshCw data-icon="inline-start" />{{ $t('检查网关状态') }}</Button>
    </header>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <template v-else>
      <Alert v-if="error" variant="destructive"><AlertDescription class="break-words">{{ error }} <Button v-if="!configuration" variant="outline" size="sm" @click="load">{{ $t('重试') }}</Button></AlertDescription></Alert>
      <Alert v-if="!capability"><Globe /><AlertDescription>{{ $t('此部署尚未配对公网连接器，请联系运维完成独立部署。') }}</AlertDescription></Alert>
      <Alert v-else-if="!capability.namespaceIsolationAvailable" variant="destructive"><AlertDescription>{{ publicGatewayFailure('GatewaySafetyCheckFailed') }}<p v-if="capability.configurationError" class="break-words">{{ capability.configurationError }}</p></AlertDescription></Alert>
      <form v-if="configuration" class="flex max-w-3xl flex-col gap-6" @submit.prevent="save">
        <FieldGroup>
          <Field orientation="horizontal">
            <Switch id="gateway-enabled" v-model="form.enabled" :disabled="!capability?.namespaceIsolationAvailable || saving" />
            <FieldContent><FieldLabel for="gateway-enabled">{{ $t('启用题目内网穿透') }}</FieldLabel><FieldDescription>{{ $t('仅公开获准的选手与练习容器，不公开 Checker、Fix Target 或题库测试容器。') }}</FieldDescription></FieldContent>
          </Field>
          <Field><FieldLabel>{{ $t('已配对连接器') }}</FieldLabel><p class="break-all font-mono text-sm">{{ capability?.connectorId ?? '—' }} · {{ capability?.runnerId ?? '—' }}</p><FieldDescription>{{ $t('连接器和端口范围由部署配置决定，页面不能指定任意代理目标。') }}</FieldDescription></Field>
          <Field :data-invalid="Boolean(fieldErrors.publicOrigin)">
            <FieldLabel for="gateway-origin">{{ $t('公网平台入口') }}</FieldLabel>
            <Select v-model="form.publicOrigin" :disabled="!capability || saving"><SelectTrigger id="gateway-origin" :aria-invalid="Boolean(fieldErrors.publicOrigin)"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="origin in capability?.approvedOrigins ?? []" :key="origin" :value="origin">{{ origin }}</SelectItem></SelectGroup></SelectContent></Select>
            <FieldError v-if="fieldErrors.publicOrigin">{{ fieldErrors.publicOrigin }}</FieldError>
          </Field>
          <Field :data-invalid="Boolean(fieldErrors.directOrigins)"><FieldLabel for="gateway-direct">{{ $t('内网平台入口') }}</FieldLabel><Textarea id="gateway-direct" v-model="form.directOrigins" :disabled="saving" :aria-invalid="Boolean(fieldErrors.directOrigins)" rows="3" placeholder="https://noctf.example.local" /><FieldDescription>{{ $t('每行一个完整入口；不同入口分别登录，不共享登录 Cookie。') }}</FieldDescription><FieldError v-if="fieldErrors.directOrigins">{{ fieldErrors.directOrigins }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.publicRuntimeHost)"><FieldLabel for="gateway-host">{{ $t('题目公网主机') }}</FieldLabel><Input id="gateway-host" v-model="form.publicRuntimeHost" :disabled="saving" :aria-invalid="Boolean(fieldErrors.publicRuntimeHost)" maxlength="253" /><FieldError v-if="fieldErrors.publicRuntimeHost">{{ fieldErrors.publicRuntimeHost }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.directRuntimeHostOverride)"><FieldLabel for="gateway-direct-host">{{ $t('内网显示主机覆盖') }}</FieldLabel><Input id="gateway-direct-host" v-model="form.directRuntimeHostOverride" :disabled="saving" :aria-invalid="Boolean(fieldErrors.directRuntimeHostOverride)" maxlength="253" /><FieldDescription>{{ $t('留空保留现有内网连接文案。') }}</FieldDescription><FieldError v-if="fieldErrors.directRuntimeHostOverride">{{ fieldErrors.directRuntimeHostOverride }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.maxPublishedPorts)"><FieldLabel for="gateway-quota">{{ $t('最大公开端口数') }}</FieldLabel><Input id="gateway-quota" v-model.number="form.maxPublishedPorts" type="number" min="1" :max="capability?.maximumPorts ?? 1" :disabled="saving" :aria-invalid="Boolean(fieldErrors.maxPublishedPorts)" /><FieldDescription>{{ $t('网关允许端口范围') }}: {{ capability?.firstPort ?? '—' }}–{{ capability?.lastPort ?? '—' }} · {{ $t('网关保留端口') }}: {{ capability?.reservedPorts?.join(', ') || '—' }}</FieldDescription><FieldError v-if="fieldErrors.maxPublishedPorts">{{ fieldErrors.maxPublishedPorts }}</FieldError></Field>
        </FieldGroup>
        <Alert v-if="dirty"><AlertDescription>{{ $t('网关变更可能中断已有公网连接，内网实例不会重启。') }}</AlertDescription></Alert>
        <div class="flex flex-wrap items-center gap-3"><Button type="submit" :disabled="saving || !capability || !dirty"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('保存修改') }}</Button><span v-if="dirty" class="text-sm text-muted-foreground">{{ $t('有未保存的网关修改') }}</span></div>
      </form>
      <Separator />
      <section class="flex min-w-0 flex-col gap-3" aria-live="polite">
        <h3 class="text-sm font-semibold">{{ $t('网关应用状态') }}</h3>
        <p v-if="statusError" class="text-sm text-destructive">{{ statusError }}</p>
        <p v-if="application.timedOut.value" class="text-sm">{{ $t('网关应用尚未完成，可再次检查状态；设置已保存。') }}</p>
        <p class="text-sm">{{ status?.applied ? $t('网关设置已应用') : $t('公网状态待确认') }}<span v-if="status?.failure"> · {{ publicGatewayFailure(status.failure) }}</span></p>
        <p v-if="!status?.runtimes?.length" class="text-sm text-muted-foreground">{{ $t('暂无题目公网发布记录') }}</p>
        <div v-for="runtime in status?.runtimes ?? []" :key="runtime.runtimeId" class="flex min-w-0 flex-col gap-2 border-b pb-3">
          <span class="break-all font-mono text-xs">{{ runtime.runtimeId }}</span>
          <p v-if="runtime.failure" class="text-sm text-destructive">{{ publicGatewayFailure(runtime.failure) }}</p>
          <div v-for="endpoint in runtime.endpoints ?? []" :key="endpoint.containerPort" class="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
            <span>{{ $t('容器端口') }} <span class="font-mono tabular-nums">{{ endpoint.containerPort }}</span></span>
            <span>{{ $t('直连端口') }} <span class="font-mono tabular-nums">{{ endpoint.hostPort }}</span></span>
            <span>{{ $t('公网端口') }} <span class="font-mono tabular-nums">{{ endpoint.publicPort ?? '—' }}</span></span>
            <Badge :variant="endpoint.state === 'Ready' ? 'default' : 'outline'">{{ publicGatewayState(endpoint.state) }}</Badge>
            <span v-if="endpoint.failure" class="text-muted-foreground">{{ publicGatewayFailure(endpoint.failure) }}</span>
          </div>
        </div>
      </section>
    </template>
  </section>
</template>
