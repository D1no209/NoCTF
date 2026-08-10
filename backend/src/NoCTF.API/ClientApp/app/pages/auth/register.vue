<script setup lang="ts">
import { registerEndpoint } from '~/api'

definePageMeta({ middleware: 'guest' })

const { configuration } = usePlatform()

const userName = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref<string | null>(null)
const pending = ref(false)
const registered = ref<{ requiresEmailVerification?: boolean } | null>(null)

async function submit() {
  error.value = null
  if (password.value !== confirmPassword.value) {
    error.value = translate('两次输入的密码不一致')
    return
  }
  pending.value = true
  try {
    const { data, error: apiError } = await registerEndpoint({
      body: { userName: userName.value, email: email.value, password: password.value },
    })
    if (apiError) throw parseApiError(apiError)
    registered.value = data ?? {}
  }
  catch (e) {
    error.value = parseApiError(e).message
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
    <Card v-if="registered">
      <CardHeader>
        <CardTitle>{{ $t('注册成功') }}</CardTitle>
        <CardDescription>
          {{ registered.requiresEmailVerification ? $t('验证邮件已发送,请查收邮箱完成验证后登录。') : $t('现在可以登录了。') }}
        </CardDescription>
      </CardHeader>
      <CardFooter>
        <Button as-child class="w-full">
          <NuxtLink to="/auth/login">{{ $t('前往登录') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
    <Card v-else>
      <CardHeader>
        <CardTitle>{{ $t('注册') }}</CardTitle>
        <CardDescription>{{ $t('创建一个新账户以参加竞赛') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="userName">{{ $t('用户名') }}</FieldLabel>
              <Input id="userName" v-model="userName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="email">{{ $t('邮箱') }}</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('密码') }}</FieldLabel>
              <Input id="password" v-model="password" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">{{ $t('确认密码') }}</FieldLabel>
              <Input id="confirmPassword" v-model="confirmPassword" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('注册') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground"> {{ $t('已有账户?') }} <NuxtLink to="/auth/login" class="ml-1 underline">{{ $t('直接登录') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
