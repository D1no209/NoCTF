<script setup lang="ts">
import { KeyRound, Send } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformGetEmailVerificationConfiguration,
  adminPlatformReplaceEmailVerificationPassword,
  adminPlatformSendEmailVerificationTest,
  adminPlatformUpdateEmailVerificationConfiguration,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse,
  NoCtfapiEndpointsAdministrationPlatformSmtpSecurityModeProtocol,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type EmailConfiguration = NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse

const configuration = ref<EmailConfiguration | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

const form = reactive({
  enabled: false,
  publicBaseUrl: '',
  tokenLifetimeMinutes: 30,
  resendCooldownSeconds: 60,
  passwordResetTokenLifetimeMinutes: 30,
  passwordResetCooldownSeconds: 60,
  passwordResetMaxRequestsPerHour: 5,
  smtpHost: '',
  smtpPort: 587,
  smtpSecurityMode: 'StartTls' as NoCtfapiEndpointsAdministrationPlatformSmtpSecurityModeProtocol,
  smtpUserName: '',
  smtpFromAddress: '',
  smtpFromName: '',
  smtpTimeoutSeconds: 15,
})
const saving = ref(false)

const passwordOpen = ref(false)
const newPassword = ref('')
const passwordSaving = ref(false)

const sendingTest = ref(false)

function syncForm(value: EmailConfiguration): void {
  form.enabled = value.enabled ?? false
  form.publicBaseUrl = value.publicBaseUrl ?? ''
  form.tokenLifetimeMinutes = value.tokenLifetimeMinutes ?? 30
  form.resendCooldownSeconds = value.resendCooldownSeconds ?? 60
  form.passwordResetTokenLifetimeMinutes = value.passwordResetTokenLifetimeMinutes ?? 30
  form.passwordResetCooldownSeconds = value.passwordResetCooldownSeconds ?? 60
  form.passwordResetMaxRequestsPerHour = value.passwordResetMaxRequestsPerHour ?? 5
  form.smtpHost = value.smtpHost ?? ''
  form.smtpPort = value.smtpPort ?? 587
  form.smtpSecurityMode = value.smtpSecurityMode ?? 'StartTls'
  form.smtpUserName = value.smtpUserName ?? ''
  form.smtpFromAddress = value.smtpFromAddress ?? ''
  form.smtpFromName = value.smtpFromName ?? ''
  form.smtpTimeoutSeconds = value.smtpTimeoutSeconds ?? 15
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminPlatformGetEmailVerificationConfiguration()
  loading.value = false
  if (error || !data) {
    loadError.value = parseApiError(error).message
    return
  }
  configuration.value = data
  syncForm(data)
}

async function save(): Promise<void> {
  if (!configuration.value) return
  saving.value = true
  const { data, error, response } = await adminPlatformUpdateEmailVerificationConfiguration({
    body: { ...form, expectedRevision: configuration.value.revision ?? 0 },
  })
  saving.value = false
  if (error) {
    if (response?.status === 409) {
      toast.error(translate("配置已被他人修改,请刷新后重试"))
      await load()
    }
    else {
      toast.error(parseApiError(error).message)
    }
    return
  }
  if (data) configuration.value = data
  toast.success(translate("邮箱验证配置已保存"))
}

async function replacePassword(): Promise<void> {
  if (!configuration.value || !newPassword.value) return
  passwordSaving.value = true
  const { data, error, response } = await adminPlatformReplaceEmailVerificationPassword({
    body: { password: newPassword.value, expectedRevision: configuration.value.revision ?? 0 },
  })
  passwordSaving.value = false
  if (error) {
    if (response?.status === 409) {
      toast.error(translate("配置已被他人修改,请刷新后重试"))
      passwordOpen.value = false
      await load()
    }
    else {
      toast.error(parseApiError(error).message)
    }
    return
  }
  passwordOpen.value = false
  newPassword.value = ''
  if (data) configuration.value = data
  toast.success(translate("SMTP 密码已更新"))
}

async function sendTest(): Promise<void> {
  sendingTest.value = true
  const { error } = await adminPlatformSendEmailVerificationTest()
  sendingTest.value = false
  if (error) {
    toast.error(parseApiError(error, translate("测试邮件发送失败")).message)
    return
  }
  toast.success(translate("测试邮件已发送至当前管理员邮箱"))
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-96 w-full" />

    <template v-else-if="configuration">
      <Card>
        <CardHeader class="flex flex-row items-center justify-between gap-4">
          <div>
            <CardTitle>{{ $t('邮箱验证配置') }}</CardTitle>
            <CardDescription>
              {{ $t('注册验证与密码重置邮件（修订版本 {revision}，更新于', { revision: configuration.revision ?? 0 }) }}
              <AdminDateTime :value="configuration.updatedAt" />）
            </CardDescription>
          </div>
          <div class="flex items-center gap-2">
            <Switch id="email-enabled" v-model="form.enabled" />
            <Label for="email-enabled">{{ $t('启用邮件发送') }}</Label>
          </div>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="public-base-url">{{ $t('公开访问地址') }}</FieldLabel>
                <Input id="public-base-url" v-model="form.publicBaseUrl" required placeholder="https://ctf.example.com" />
                <FieldDescription>{{ $t('用于生成邮件中的验证/重置链接。') }}</FieldDescription>
              </Field>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="token-lifetime">{{ $t('验证令牌有效期(分钟)') }}</FieldLabel>
                  <Input id="token-lifetime" v-model.number="form.tokenLifetimeMinutes" type="number" min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="resend-cooldown">{{ $t('重发冷却(秒)') }}</FieldLabel>
                  <Input id="resend-cooldown" v-model.number="form.resendCooldownSeconds" type="number" min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="smtp-timeout">{{ $t('SMTP 超时(秒)') }}</FieldLabel>
                  <Input id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds" type="number" min="1" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="reset-lifetime">{{ $t('重置令牌有效期(分钟)') }}</FieldLabel>
                  <Input id="reset-lifetime" v-model.number="form.passwordResetTokenLifetimeMinutes" type="number" min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-cooldown">{{ $t('重置冷却(秒)') }}</FieldLabel>
                  <Input id="reset-cooldown" v-model.number="form.passwordResetCooldownSeconds" type="number" min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-max">{{ $t('重置频率上限(次/小时)') }}</FieldLabel>
                  <Input id="reset-max" v-model.number="form.passwordResetMaxRequestsPerHour" type="number" min="1" required />
                </Field>
              </div>

              <FieldSeparator>{{ $t('SMTP 服务器') }}</FieldSeparator>

              <div class="grid gap-4 sm:grid-cols-3">
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-host">{{ $t('主机') }}</FieldLabel>
                  <Input id="smtp-host" v-model="form.smtpHost" required placeholder="smtp.example.com" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-port">{{ $t('端口') }}</FieldLabel>
                  <Input id="smtp-port" v-model.number="form.smtpPort" type="number" min="1" max="65535" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="smtp-security">{{ $t('加密方式') }}</FieldLabel>
                  <Select v-model="form.smtpSecurityMode">
                    <SelectTrigger id="smtp-security" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="None">{{ $t('无加密') }}</SelectItem>
                        <SelectItem value="SslOnConnect">SSL/TLS</SelectItem>
                        <SelectItem value="StartTls">STARTTLS</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-username">{{ $t('用户名') }}</FieldLabel>
                  <Input id="smtp-username" v-model="form.smtpUserName" required autocomplete="off" />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel for="smtp-from-address">{{ $t('发件地址') }}</FieldLabel>
                  <Input id="smtp-from-address" v-model="form.smtpFromAddress" type="email" required placeholder="noreply@example.com" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-from-name">{{ $t('发件人名称') }}</FieldLabel>
                  <Input id="smtp-from-name" v-model="form.smtpFromName" required placeholder="NoCTF" />
                </Field>
              </div>
              <Field orientation="horizontal" class="flex-wrap gap-4">
                <Button type="submit" :disabled="saving">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('保存配置') }} </Button>
                <Button type="button" variant="outline" @click="passwordOpen = true">
                  <KeyRound data-icon="inline-start" /> {{ $t('更换 SMTP 密码') }} <Badge :variant="configuration.smtpPasswordConfigured ? 'secondary' : 'destructive'" class="ml-2">
                    {{ configuration.smtpPasswordConfigured ? $t('已配置') : $t('未配置') }}
                  </Badge>
                </Button>
                <Button type="button" variant="outline" :disabled="sendingTest || !form.enabled" @click="sendTest">
                  <Spinner v-if="sendingTest" data-icon="inline-start" />
                  <Send v-else data-icon="inline-start" /> {{ $t('发送测试邮件') }} </Button>
              </Field>
              <p class="text-sm text-muted-foreground"> {{ $t('测试邮件将发送到当前登录管理员的邮箱地址;未启用邮件发送时不可用。') }} </p>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </template>

    <Dialog v-model:open="passwordOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('更换 SMTP 密码') }}</DialogTitle>
          <DialogDescription>{{ $t('密码仅用于 SMTP 认证,保存后平台不再回显。') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="smtp-password">{{ $t('新密码') }}</FieldLabel>
            <PasswordInput id="smtp-password" v-model="newPassword" required autocomplete="new-password" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="passwordOpen = false">{{ $t('取消') }}</Button>
          <Button :disabled="passwordSaving || !newPassword" @click="replacePassword">
            <Spinner v-if="passwordSaving" data-icon="inline-start" /> {{ $t('保存密码') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
