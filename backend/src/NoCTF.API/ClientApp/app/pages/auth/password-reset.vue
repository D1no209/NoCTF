<script setup lang="ts">
import { authenticationCompletePasswordReset, authenticationRequestPasswordReset } from '~/api'

definePageMeta({ middleware: 'guest', alias: ['/reset-password'] })

const route = useRoute()
const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : null))

// Request stage
const email = ref('')
const requested = ref(false)

// Complete stage
const newPassword = ref('')
const confirmPassword = ref('')
const completed = ref(false)

const error = ref<string | null>(null)
const pending = ref(false)

async function requestReset() {
  error.value = null
  pending.value = true
  try {
    const { error: apiError } = await authenticationRequestPasswordReset({ body: { email: email.value } })
    if (apiError) throw parseApiError(apiError)
    requested.value = true
  }
  catch (e) {
    error.value = parseApiError(e).message
  }
  finally {
    pending.value = false
  }
}

async function completeReset() {
  error.value = null
  if (newPassword.value !== confirmPassword.value) {
    error.value = '两次输入的密码不一致'
    return
  }
  pending.value = true
  try {
    const { error: apiError } = await authenticationCompletePasswordReset({
      body: { token: token.value!, newPassword: newPassword.value },
    })
    if (apiError) throw parseApiError(apiError)
    completed.value = true
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
    <!-- Complete with token -->
    <Card v-if="token">
      <CardHeader>
        <CardTitle>设置新密码</CardTitle>
        <CardDescription>输入你的新密码以完成重置</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="completed" class="mb-4">
          <AlertDescription>密码已重置,请使用新密码登录。</AlertDescription>
        </Alert>
        <form v-if="!completed" @submit.prevent="completeReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="newPassword">新密码</FieldLabel>
              <Input id="newPassword" v-model="newPassword" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">确认新密码</FieldLabel>
              <Input id="confirmPassword" v-model="confirmPassword" type="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" />
                重置密码
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter>
        <Button as-child variant="outline" class="w-full">
          <NuxtLink to="/auth/login">前往登录</NuxtLink>
        </Button>
      </CardFooter>
    </Card>

    <!-- Request reset email -->
    <Card v-else>
      <CardHeader>
        <CardTitle>重置密码</CardTitle>
        <CardDescription>输入注册邮箱,我们将发送重置链接</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="requested" class="mb-4">
          <AlertDescription>如果该邮箱已注册,重置邮件已发送,请按邮件指引操作。</AlertDescription>
        </Alert>
        <form v-if="!requested" @submit.prevent="requestReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="email">邮箱</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" />
                发送重置邮件
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground">
        <NuxtLink to="/auth/login" class="underline">返回登录</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
