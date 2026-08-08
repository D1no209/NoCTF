<script setup lang="ts">
import { registerEndpoint } from '~/api'

definePageMeta({ middleware: 'guest' })

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
    error.value = '两次输入的密码不一致'
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
    <Card v-if="registered">
      <CardHeader>
        <CardTitle>注册成功</CardTitle>
        <CardDescription>
          {{ registered.requiresEmailVerification ? '验证邮件已发送,请查收邮箱完成验证后登录。' : '现在可以登录了。' }}
        </CardDescription>
      </CardHeader>
      <CardFooter>
        <Button as-child class="w-full">
          <NuxtLink to="/auth/login">前往登录</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
    <Card v-else>
      <CardHeader>
        <CardTitle>注册</CardTitle>
        <CardDescription>创建一个新账户以参加竞赛</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="userName">用户名</FieldLabel>
              <Input id="userName" v-model="userName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="email">邮箱</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
            </Field>
            <Field>
              <FieldLabel for="password">密码</FieldLabel>
              <Input id="password" v-model="password" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">确认密码</FieldLabel>
              <Input id="confirmPassword" v-model="confirmPassword" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" />
                注册
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground">
        已有账户?
        <NuxtLink to="/auth/login" class="ml-1 underline">直接登录</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
