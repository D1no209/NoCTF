<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  authenticationUpdateMyProfile,
  authenticationUploadMyAvatar,
  changePasswordEndpoint,
} from '~/api'

definePageMeta({ middleware: 'auth' })

const { user, fetchMe, logoutAll } = useAuth()

// Profile tab
const description = ref(user.value?.description ?? '')
const isEmailPublic = ref(user.value?.isEmailPublic ?? false)
const profilePending = ref(false)

watch(user, (u) => {
  description.value = u?.description ?? ''
  isEmailPublic.value = u?.isEmailPublic ?? false
})

async function saveProfile() {
  profilePending.value = true
  try {
    const { error } = await authenticationUpdateMyProfile({
      body: { description: description.value || null, isEmailPublic: isEmailPublic.value },
    })
    if (error) throw parseApiError(error)
    await fetchMe()
    toast.success('资料已保存')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    profilePending.value = false
  }
}

// Avatar
const avatarInput = ref<HTMLInputElement | null>(null)
const avatarPending = ref(false)
const avatarEditorOpen = ref(false)
const avatarSourceFile = ref<File | null>(null)

function selectAvatar(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file)
    return
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 12 * 1024 * 1024) {
    toast.error('请选择不超过 12 MiB 的 JPEG、PNG 或 WebP 图片')
    return
  }
  avatarSourceFile.value = file
  avatarEditorOpen.value = true
}

function setAvatarEditorOpen(open: boolean) {
  if (avatarPending.value)
    return
  avatarEditorOpen.value = open
  if (!open)
    avatarSourceFile.value = null
}

async function uploadAvatar(file: File) {
  avatarPending.value = true
  try {
    const { error } = await authenticationUploadMyAvatar({ body: { file } })
    if (error) throw parseApiError(error)
    await fetchMe()
    avatarEditorOpen.value = false
    avatarSourceFile.value = null
    toast.success('头像已更新')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    avatarPending.value = false
  }
}

// Password tab
const currentPassword = ref('')
const newPassword = ref('')
const confirmNewPassword = ref('')
const passwordPending = ref(false)

async function changePassword() {
  if (newPassword.value !== confirmNewPassword.value) {
    toast.error('两次输入的新密码不一致')
    return
  }
  passwordPending.value = true
  try {
    const { error } = await changePasswordEndpoint({
      body: { currentPassword: currentPassword.value, newPassword: newPassword.value },
    })
    if (error) throw parseApiError(error)
    toast.success('密码已修改,请重新登录')
    await logoutAll()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    passwordPending.value = false
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-8">
    <h1 class="text-2xl font-semibold">账户设置</h1>
    <Tabs default-value="profile">
      <TabsList>
        <TabsTrigger value="profile">资料</TabsTrigger>
        <TabsTrigger value="password">密码</TabsTrigger>
        <TabsTrigger value="security">安全</TabsTrigger>
      </TabsList>

      <TabsContent value="profile">
        <Card>
          <CardHeader>
            <CardTitle>个人资料</CardTitle>
            <CardDescription>{{ user?.userName }} · {{ user?.email }}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup>
              <Field>
                <FieldLabel>头像</FieldLabel>
                <div class="flex items-center gap-4">
                  <Avatar class="size-16">
                    <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
                    <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                  </Avatar>
                  <div class="flex flex-col items-start gap-2">
                    <input
                      ref="avatarInput"
                      type="file"
                      accept="image/jpeg,image/png,image/webp"
                      class="sr-only"
                      @change="selectAvatar"
                    >
                    <Button variant="outline" :disabled="avatarPending" @click="avatarInput?.click()">
                      <Spinner v-if="avatarPending" data-icon="inline-start" />
                      选择并裁剪头像
                    </Button>
                    <span class="text-xs text-muted-foreground">JPEG、PNG 或 WebP，原图不超过 12 MiB</span>
                  </div>
                </div>
              </Field>
              <Field>
                <FieldLabel for="description">个人简介</FieldLabel>
                <Textarea id="description" v-model="description" rows="4" placeholder="介绍一下自己(可选)" />
              </Field>
              <Field orientation="horizontal">
                <Switch id="isEmailPublic" v-model="isEmailPublic" />
                <FieldLabel for="isEmailPublic">在个人资料页公开邮箱</FieldLabel>
              </Field>
            </FieldGroup>
          </CardContent>
          <CardFooter>
            <Button :disabled="profilePending" @click="saveProfile">
              <Spinner v-if="profilePending" data-icon="inline-start" />
              保存资料
            </Button>
          </CardFooter>
        </Card>
      </TabsContent>

      <TabsContent value="password">
        <Card>
          <CardHeader>
            <CardTitle>修改密码</CardTitle>
            <CardDescription>修改成功后所有设备都需要重新登录</CardDescription>
          </CardHeader>
          <CardContent>
            <form @submit.prevent="changePassword">
              <FieldGroup>
                <Field>
                  <FieldLabel for="currentPassword">当前密码</FieldLabel>
                  <Input id="currentPassword" v-model="currentPassword" type="password" autocomplete="current-password" required />
                </Field>
                <Field>
                  <FieldLabel for="newPassword">新密码</FieldLabel>
                  <Input id="newPassword" v-model="newPassword" type="password" autocomplete="new-password" required />
                </Field>
                <Field>
                  <FieldLabel for="confirmNewPassword">确认新密码</FieldLabel>
                  <Input id="confirmNewPassword" v-model="confirmNewPassword" type="password" autocomplete="new-password" required />
                </Field>
              </FieldGroup>
            </form>
          </CardContent>
          <CardFooter>
            <Button :disabled="passwordPending" @click="changePassword">
              <Spinner v-if="passwordPending" data-icon="inline-start" />
              修改密码
            </Button>
          </CardFooter>
        </Card>
      </TabsContent>

      <TabsContent value="security">
        <Card>
          <CardHeader>
            <CardTitle>安全</CardTitle>
            <CardDescription>管理你的登录会话</CardDescription>
          </CardHeader>
          <CardContent>
            <Alert variant="destructive">
              <AlertTitle>全局注销</AlertTitle>
              <AlertDescription>
                吊销你在所有设备上的会话(包括本机),之后需要重新登录。
              </AlertDescription>
            </Alert>
          </CardContent>
          <CardFooter>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">注销所有会话</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>确认注销所有会话?</AlertDialogTitle>
                  <AlertDialogDescription>所有设备上的登录状态将立即失效。</AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>取消</AlertDialogCancel>
                  <AlertDialogAction @click="logoutAll">确认注销</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </CardFooter>
        </Card>
      </TabsContent>
    </Tabs>
    <AvatarCropDialog
      :open="avatarEditorOpen"
      :file="avatarSourceFile"
      :saving="avatarPending"
      @update:open="setAvatarEditorOpen"
      @save="uploadAvatar"
      @error="toast.error($event.message)"
    />
  </div>
</template>
