<script setup lang="ts">
import { resendEmailVerificationEndpoint, verifyEmailEndpoint } from '~/api'

const route = useRoute()
const { isLoggedIn } = useAuth()

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
    message.value = parseApiError(error, '验证链接无效或已过期').message
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
    <Card>
      <CardHeader>
        <CardTitle>邮箱验证</CardTitle>
        <CardDescription v-if="state === 'verifying'">正在验证你的邮箱…</CardDescription>
        <CardDescription v-else-if="state === 'success'">邮箱验证成功,现在可以正常使用全部功能。</CardDescription>
        <CardDescription v-else-if="token && state === 'failed'">验证失败</CardDescription>
        <CardDescription v-else>平台要求验证邮箱后才能继续使用。</CardDescription>
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
            <AlertDescription>验证邮件已重新发送,请查收。</AlertDescription>
          </Alert>
        </FieldGroup>
      </CardContent>
      <CardFooter class="flex flex-col gap-2">
        <Button v-if="state === 'success'" as-child class="w-full">
          <NuxtLink to="/auth/login">前往登录</NuxtLink>
        </Button>
        <Button
          v-if="isLoggedIn && state !== 'success'"
          variant="outline"
          class="w-full"
          :disabled="resendPending || resendDone"
          @click="resend"
        >
          <Spinner v-if="resendPending" data-icon="inline-start" />
          重新发送验证邮件
        </Button>
        <Button v-if="!isLoggedIn && !token" as-child variant="outline" class="w-full">
          <NuxtLink to="/auth/login">返回登录</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>
