<script setup lang="ts">
import { LockKeyhole } from '@lucide/vue'
import { authenticationGetMySchoolIdentity, authenticationUpdateMySchoolIdentity } from '~/api'

const emit = defineEmits<{ dirty: [value: boolean] }>()
const fullName = ref('')
const studentNumber = ref('')
const saved = ref({ fullName: '', studentNumber: '' })
const loading = ref(true)
const loaded = ref(false)
const pending = ref(false)
const retentionDays = ref<number | null>(null)
const error = ref<string | null>(null)
const success = ref(false)
const fieldErrors = ref<Record<string, string[]>>({})
const fieldError = (name: string) => Object.entries(fieldErrors.value).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]?.join(' ')
const dirty = computed(() => fullName.value !== saved.value.fullName || studentNumber.value !== saved.value.studentNumber)
watch(dirty, value => { emit('dirty', value); if (value) success.value = false })

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await authenticationGetMySchoolIdentity()
    if (result.error || !result.data) throw result.error
    fullName.value = result.data.fullName ?? ''
    studentNumber.value = result.data.studentNumber ?? ''
    retentionDays.value = result.data.ipRetentionDays ?? null
    saved.value = { fullName: fullName.value, studentNumber: studentNumber.value }
    loaded.value = true
  }
  catch (e) { error.value = parseApiError(e).message }
  finally { loading.value = false }
}
async function save() {
  if (!loaded.value || pending.value) return
  pending.value = true
  error.value = null
  success.value = false
  fieldErrors.value = {}
  const snapshot = { fullName: fullName.value.trim(), studentNumber: studentNumber.value.trim() }
  try {
    const result = await authenticationUpdateMySchoolIdentity({ body: snapshot })
    if (result.error) throw result.error
    fullName.value = snapshot.fullName
    studentNumber.value = snapshot.studentNumber
    saved.value = snapshot
    success.value = true
  }
  catch (e) {
    const parsed = parseApiError(e)
    error.value = parsed.message
    fieldErrors.value = parsed.fieldErrors ?? {}
  }
  finally { pending.value = false }
}
onMounted(load)
</script>

<template>
  <section class="flex flex-col gap-6">
    <header class="flex flex-col gap-2">
      <h2 class="flex items-center gap-2 text-xl font-semibold"><LockKeyhole class="size-5" />{{ $t('个人信息') }}</h2>
      <p class="max-w-prose text-sm text-muted-foreground">{{ $t('仅用于赛事身份核验，不公开，仅平台管理员及你参加比赛的授权管理人员可见。') }}</p>
      <p class="text-sm text-muted-foreground">{{ $t('以下信息由你自行填写，不代表已实名认证。选填或清空均不影响参赛。') }}</p>
    </header>
    <Skeleton v-if="loading" class="h-40 w-full" />
    <Alert v-else-if="!loaded" variant="destructive"><AlertDescription>{{ error }}<Button variant="outline" @click="load">{{ $t('重试') }}</Button></AlertDescription></Alert>
    <form v-else class="flex flex-col gap-6" @submit.prevent="save">
      <FieldGroup>
        <Field :data-invalid="Boolean(fieldError('FullName'))">
          <FieldLabel for="school-name">{{ $t('姓名（选填）') }}</FieldLabel>
          <Input id="school-name" v-model="fullName" :disabled="pending" :aria-invalid="Boolean(fieldError('FullName'))" aria-describedby="school-name-error" maxlength="100" autocomplete="off" />
          <FieldError id="school-name-error">{{ fieldError('FullName') }}</FieldError>
        </Field>
        <Field :data-invalid="Boolean(fieldError('StudentNumber'))">
          <FieldLabel for="student-number">{{ $t('学号（选填）') }}</FieldLabel>
          <Input id="student-number" v-model="studentNumber" :disabled="pending" :aria-invalid="Boolean(fieldError('StudentNumber'))" aria-describedby="student-number-error" type="text" maxlength="64" autocomplete="off" />
          <FieldError id="student-number-error">{{ fieldError('StudentNumber') }}</FieldError>
          <FieldDescription>{{ $t('支持字母和数字，保留开头的 0。留空保存即可清除。') }}</FieldDescription>
        </Field>
      </FieldGroup>
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ error }}</AlertDescription></Alert>
      <div class="flex flex-wrap items-center gap-3">
        <Button type="submit" :disabled="pending || !dirty"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('保存个人信息') }}</Button>
        <Button v-if="error && !dirty" type="button" variant="outline" @click="load">{{ $t('重试') }}</Button>
        <span role="status" class="text-sm text-muted-foreground">{{ dirty ? $t('有未保存的修改') : success ? $t('已保存') : '' }}</span>
      </div>
    </form>
    <Separator />
    <p v-if="retentionDays" class="text-xs text-muted-foreground">{{ $t('为赛事核验和安全排查，平台记录注册、登录及 Flag／Patch 提交的来源 IP，留存 {days} 天。不会收集地理定位或设备指纹，相同 IP 不代表作弊。', { days: retentionDays }) }}</p>
  </section>
</template>
