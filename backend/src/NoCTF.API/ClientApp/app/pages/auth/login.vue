<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'guest' })

const route = useRoute()
const { login } = useAuth()
const { configuration } = usePlatform()

const loginName = ref('')
const password = ref('')
const error = ref<string | null>(null)
const pending = ref(false)

async function submit() {
  error.value = null
  if (!loginName.value || !password.value) {
    error.value = translate('请输入用户名/邮箱和密码')
    return
  }
  pending.value = true
  try {
    await login(loginName.value, password.value)
    toast.success(translate("登录成功"))
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await navigateTo(redirect)
  }
  catch (e) {
    error.value = parseApiError(e, translate("登录失败,请检查用户名或密码")).message
  }
  finally {
    pending.value = false
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
        <CardTitle>{{ $t('登录') }}</CardTitle>
        <CardDescription>{{ $t('使用用户名或邮箱登录你的账户') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="login">{{ $t('用户名或邮箱') }}</FieldLabel>
              <Input id="login" v-model="loginName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('密码') }}</FieldLabel>
              <PasswordInput id="password" v-model="password" autocomplete="current-password" required />
              <FieldDescription>
                <NuxtLink to="/auth/password-reset" class="underline">{{ $t('忘记密码?') }}</NuxtLink>
              </FieldDescription>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('登录') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground"> {{ $t('还没有账户?') }} <NuxtLink to="/auth/register" class="ml-1 underline">{{ $t('立即注册') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
