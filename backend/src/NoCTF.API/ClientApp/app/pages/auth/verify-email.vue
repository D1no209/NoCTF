<script setup lang="ts">
import { resendEmailVerificationEndpoint, verifyEmailEndpoint } from '~/api'

definePageMeta({ alias: ['/verify-email'] })

const route = useRoute()
const { isLoggedIn } = useAuth()
const { configuration } = usePlatform()

const state = ref<'idle' | 'verifying' | 'success' | 'failed'>('idle')
const message = ref<string | null>(null)
const resendPending = ref(false)
const resendDone = ref(false)

const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : null))

onMounted(async () => {
  if (!token.value) return
  state.value = 'verifying'
  const { error } = await verifyEmailEndpoint({ body: { token: token.value } })
  if (error) {
    state.value = 'failed'
    message.value = parseApiError(error, translate("验证链接无效或已过期")).message
  }
  else {
    state.value = 'success'
  }
})

async function resend() {
  resendPending.value = true
  const { error } = await resendEmailVerificationEndpoint()
  resendPending.value = false
  if (error) {
    message.value = parseApiError(error).message
  }
  else {
    resendDone.value = true
    message.value = null
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-md flex-col px-4 py-12">
    <div class="mb-8 flex flex-col items-center gap-2 text-center">
      <NuxtLink to="/" class="flex items-baseline gap-1.5 font-mono text-2xl font-semibold tracking-tight">
        <span class="text-primary">&gt;</span>
        <span>{{ configuration?.name ?? 'NoCTF' }}</span>
        <span class="animate-blink text-primary">_</span>
      </NuxtLink>
      <p v-if="configuration?.description" class="text-sm text-muted-foreground">{{ configuration.description }}</p>
    </div>
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('邮箱验证') }}</CardTitle>
        <CardDescription v-if="state === 'verifying'">{{ $t('正在验证你的邮箱…') }}</CardDescription>
        <CardDescription v-else-if="state === 'success'">{{ $t('邮箱验证成功,现在可以正常使用全部功能。') }}</CardDescription>
        <CardDescription v-else-if="token && state === 'failed'">{{ $t('验证失败') }}</CardDescription>
        <CardDescription v-else>{{ $t('平台要求验证邮箱后才能继续使用。') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldGroup>
          <div v-if="state === 'verifying'" class="flex justify-center py-4">
            <Spinner class="size-6" />
          </div>
          <Alert v-if="message" :variant="state === 'failed' ? 'destructive' : 'default'">
            <AlertDescription>{{ message }}</AlertDescription>
          </Alert>
          <Alert v-if="resendDone">
            <AlertDescription>{{ $t('验证邮件已重新发送,请查收。') }}</AlertDescription>
          </Alert>
        </FieldGroup>
      </CardContent>
      <CardFooter class="flex flex-col gap-2">
        <Button v-if="state === 'success'" as-child class="w-full">
          <NuxtLink to="/auth/login">{{ $t('前往登录') }}</NuxtLink>
        </Button>
        <Button
          v-if="isLoggedIn && state !== 'success'"
          variant="outline"
          class="w-full"
          :disabled="resendPending || resendDone"
          @click="resend"
        >
          <Spinner v-if="resendPending" data-icon="inline-start" /> {{ $t('重新发送验证邮件') }} </Button>
        <Button v-if="!isLoggedIn && !token" as-child variant="outline" class="w-full">
          <NuxtLink to="/auth/login">{{ $t('返回登录') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>
