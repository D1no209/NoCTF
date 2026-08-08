<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'guest' })

const route = useRoute()
const { login } = useAuth()

const loginName = ref('')
const password = ref('')
const error = ref<string | null>(null)
const pending = ref(false)

async function submit() {
  error.value = null
  if (!loginName.value || !password.value) {
    error.value = '请输入用户名/邮箱和密码'
    return
  }
  pending.value = true
  try {
    await login(loginName.value, password.value)
    toast.success('登录成功')
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await navigateTo(redirect)
  }
  catch (e) {
    error.value = parseApiError(e, '登录失败,请检查用户名或密码').message
  }
  finally {
    pending.value = false
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-md flex-col px-4 py-12">
    <Card>
      <CardHeader>
        <CardTitle>登录</CardTitle>
        <CardDescription>使用用户名或邮箱登录你的账户</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="login">用户名或邮箱</FieldLabel>
              <Input id="login" v-model="loginName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="password">密码</FieldLabel>
              <Input id="password" v-model="password" type="password" autocomplete="current-password" required />
              <FieldDescription>
                <NuxtLink to="/auth/password-reset" class="underline">忘记密码?</NuxtLink>
              </FieldDescription>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" />
                登录
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground">
        还没有账户?
        <NuxtLink to="/auth/register" class="ml-1 underline">立即注册</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
