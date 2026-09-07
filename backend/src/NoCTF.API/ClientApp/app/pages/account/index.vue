<script setup lang="ts">
import { UserRound, LockKeyhole, ShieldCheck } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  authenticationUpdateMyProfile,
  authenticationUploadMyAvatar,
  changePasswordEndpoint,
  resendEmailVerificationEndpoint,
} from '~/api'

definePageMeta({ middleware: 'auth' })

const { user, fetchMe, logoutAll } = useAuth()

// Profile tab
const description = ref(user.value?.description ?? '')
const profilePending = ref(false)
const savedDescription = ref(description.value)
const profileError = ref<string | null>(null)
const profileSuccess = ref(false)
const profileDirty = computed(() => description.value !== savedDescription.value)
const schoolDirty = ref(false)
const passwordError = ref<string | null>(null)
const emailPending = ref(false)
const emailMessage = ref<string | null>(null)
const emailError = ref(false)
async function resendEmail() {
  emailPending.value = true
  emailMessage.value = null
  emailError.value = false
  try {
    const result = await resendEmailVerificationEndpoint()
    if (result.error) throw result.error
    emailMessage.value = translate('验证邮件请求已提交，请检查邮箱。')
  } catch (e) { emailError.value = true; emailMessage.value = parseApiError(e).message }
  finally { emailPending.value = false }
}

watch(user, (u) => {
  if (!profileDirty.value) {
    description.value = u?.description ?? ''
    savedDescription.value = description.value
  }
})

async function saveProfile() {
  profilePending.value = true
  profileError.value = null
  profileSuccess.value = false
  const draft = description.value
  try {
    const { data, error } = await authenticationUpdateMyProfile({
      body: { description: draft || null },
    })
    if (error || !data) throw parseApiError(error)
    savedDescription.value = draft
    user.value = data
    profileSuccess.value = true
    toast.success(translate("资料已保存"))
  }
  catch (e) {
    profileError.value = parseApiError(e).message
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
    toast.error(translate("请选择不超过 12 MiB 的 JPEG、PNG 或 WebP 图片"))
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
    toast.success(translate("头像已更新"))
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
  passwordError.value = null
  if (newPassword.value !== confirmNewPassword.value) {
    passwordError.value = translate("两次输入的新密码不一致")
    return
  }
  passwordPending.value = true
  try {
    const { error } = await changePasswordEndpoint({
      body: { currentPassword: currentPassword.value, newPassword: newPassword.value },
    })
    if (error) throw parseApiError(error)
    toast.success(translate("密码已修改,请重新登录"))
    currentPassword.value = ''
    newPassword.value = ''
    confirmNewPassword.value = ''
    await logoutAll()
  }
  catch (e) {
    passwordError.value = parseApiError(e).message
  }
  finally {
    passwordPending.value = false
  }
}
const hasUnsaved = computed(() => profileDirty.value || schoolDirty.value || Boolean(currentPassword.value || newPassword.value || confirmNewPassword.value))
onBeforeRouteLeave(() => !hasUnsaved.value || window.confirm(translate('存在未保存的修改，确定离开吗？')))
function beforeUnload(event: BeforeUnloadEvent) {
  if (hasUnsaved.value) { event.preventDefault(); event.returnValue = '' }
}
onMounted(() => window.addEventListener('beforeunload', beforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))
</script>


<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-8 md:py-12">
    <header class="flex flex-col gap-2">
      <h1 class="text-display text-2xl">{{ $t('账户设置') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('公开资料、私密信息与账户安全分别管理。') }}</p>
    </header>
    <Separator />
    <Tabs default-value="profile" orientation="vertical" class="flex-col gap-8 md:flex-row md:gap-12">
      <TabsList variant="line" class="w-full shrink-0 items-stretch md:w-48">
        <TabsTrigger value="profile" class="justify-start gap-2"><UserRound />{{ $t('公开资料') }}<span v-if="profileDirty" :aria-label="$t('有未保存的修改')">•</span></TabsTrigger>
        <TabsTrigger value="school" class="justify-start gap-2"><LockKeyhole />{{ $t('校级比赛信息') }}<span v-if="schoolDirty" :aria-label="$t('有未保存的修改')">•</span></TabsTrigger>
        <TabsTrigger value="security" class="justify-start gap-2"><ShieldCheck />{{ $t('账户安全') }}</TabsTrigger>
      </TabsList>
      <div class="min-w-0 flex-1">
        <TabsContent value="profile" class="m-0">
          <form class="flex flex-col gap-6" @submit.prevent="saveProfile">
            <header class="flex flex-col gap-2"><h2 class="text-xl font-semibold">{{ $t('公开资料') }}</h2><p class="text-sm text-muted-foreground">{{ $t('头像、用户名和简介会展示在公开个人主页及赛事中。') }}</p></header>
            <FieldGroup>
              <Field>
                <FieldLabel>{{ $t('头像') }}</FieldLabel>
                <div class="flex flex-wrap items-center gap-4">
                  <Avatar class="size-16"><AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" /><AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback></Avatar>
                  <div class="flex flex-col items-start gap-2">
                    <input ref="avatarInput" type="file" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectAvatar">
                    <Button type="button" variant="outline" :disabled="avatarPending" @click="avatarInput?.click()"><Spinner v-if="avatarPending" data-icon="inline-start" />{{ $t('选择并裁剪头像') }}</Button>
                    <span class="text-xs text-muted-foreground">{{ $t('裁剪确认后单独保存头像，不影响其他草稿。') }}</span>
                  </div>
                </div>
              </Field>
              <Field><FieldLabel for="account-username">{{ $t('用户名') }}</FieldLabel><Input id="account-username" :model-value="user?.userName ?? ''" readonly /><FieldDescription>{{ $t('用户名为公开账户标识，此处不可修改。') }}</FieldDescription></Field>
              <Field><FieldLabel for="description">{{ $t('个人简介') }}</FieldLabel><Textarea id="description" v-model="description" :disabled="profilePending" maxlength="500" rows="5" :placeholder="$t('介绍一下自己(可选)')" /><FieldDescription>{{ description.length }} / 500</FieldDescription></Field>
            </FieldGroup>
            <Alert v-if="profileError" variant="destructive"><AlertDescription>{{ profileError }}</AlertDescription></Alert>
            <div class="flex flex-wrap items-center gap-3">
              <Button type="submit" :disabled="profilePending || !profileDirty"><Spinner v-if="profilePending" data-icon="inline-start" />{{ $t('保存公开资料') }}</Button>
              <span role="status" class="text-sm text-muted-foreground">{{ profileDirty ? $t('有未保存的修改') : profileSuccess ? $t('已保存') : '' }}</span>
            </div>
          </form>
        </TabsContent>
        <TabsContent value="school" force-mount class="m-0 data-[state=inactive]:hidden"><SchoolIdentityForm @dirty="schoolDirty = $event" /></TabsContent>
        <TabsContent value="security" class="m-0">
          <section class="flex flex-col gap-8">
            <header><h2 class="text-xl font-semibold">{{ $t('账户安全') }}</h2></header>
            <section class="flex flex-col gap-4">
              <h3 class="font-semibold">{{ $t('邮箱验证') }}</h3>
              <p class="flex flex-wrap items-center gap-2 break-all">{{ user?.email }}<Badge :variant="user?.emailVerified ? 'secondary' : 'outline'">{{ user?.emailVerified ? $t('已验证') : $t('未验证') }}</Badge></p>
              <div v-if="!user?.emailVerified" class="flex flex-wrap gap-2"><Button variant="outline" :disabled="emailPending" @click="resendEmail"><Spinner v-if="emailPending" data-icon="inline-start" />{{ $t('发送验证邮件') }}</Button><Button variant="ghost" @click="fetchMe">{{ $t('刷新验证状态') }}</Button></div>
              <Alert v-if="emailMessage" :variant="emailError ? 'destructive' : 'default'"><AlertDescription>{{ emailMessage }}</AlertDescription></Alert>
            </section>
            <Separator />
            <form class="flex flex-col gap-5" @submit.prevent="changePassword">
              <div><h3 class="font-semibold">{{ $t('修改密码') }}</h3><p class="mt-1 text-sm text-muted-foreground">{{ $t('修改成功后所有设备都需要重新登录') }}</p></div>
              <FieldGroup>
                <Field><FieldLabel for="currentPassword">{{ $t('当前密码') }}</FieldLabel><PasswordInput id="currentPassword" v-model="currentPassword" :disabled="passwordPending" autocomplete="current-password" required /></Field>
                <Field><FieldLabel for="newPassword">{{ $t('新密码') }}</FieldLabel><PasswordInput id="newPassword" v-model="newPassword" :disabled="passwordPending" autocomplete="new-password" required minlength="8" maxlength="1024" /></Field>
                <Field :data-invalid="Boolean(passwordError)"><FieldLabel for="confirmNewPassword">{{ $t('确认新密码') }}</FieldLabel><PasswordInput id="confirmNewPassword" v-model="confirmNewPassword" :disabled="passwordPending" :aria-invalid="Boolean(passwordError)" autocomplete="new-password" required /><FieldError v-if="passwordError">{{ passwordError }}</FieldError></Field>
              </FieldGroup>
              <div class="flex flex-wrap items-center gap-3"><Button type="submit" :disabled="passwordPending"><Spinner v-if="passwordPending" data-icon="inline-start" />{{ $t('修改密码') }}</Button><span v-if="currentPassword || newPassword || confirmNewPassword" class="text-sm text-muted-foreground">{{ $t('密码修改尚未提交') }}</span></div>
            </form>
            <Separator />
            <AlertDialog>
              <AlertDialogTrigger as-child><Button variant="outline" class="self-start">{{ $t('注销所有会话') }}</Button></AlertDialogTrigger>
              <AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('确认注销所有会话?') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('所有设备上的登录状态将立即失效。') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel><AlertDialogAction @click="logoutAll">{{ $t('确认注销') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent>
            </AlertDialog>
          </section>
        </TabsContent>
      </div>
    </Tabs>
    <AvatarCropDialog :open="avatarEditorOpen" :file="avatarSourceFile" :saving="avatarPending" @update:open="setAvatarEditorOpen" @save="uploadAvatar" @error="toast.error($event.message)" />
  </div>
</template>
