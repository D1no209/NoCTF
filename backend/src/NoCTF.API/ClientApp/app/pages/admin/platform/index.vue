<script setup lang="ts">
import { Upload } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformGetConfiguration,
  adminPlatformGetInformation,
  adminPlatformUpdateConfiguration,
  adminPlatformUploadLogo,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformConfigurationResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformInformationResponse,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

const { configuration: globalConfiguration } = usePlatform()

const information = ref<NoCtfapiEndpointsAdministrationPlatformPlatformInformationResponse | null>(null)
const configuration = ref<NoCtfapiEndpointsAdministrationPlatformPlatformConfigurationResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

const name = ref('')
const description = ref('')
const saving = ref(false)

const logoInput = ref<HTMLInputElement | null>(null)
const logoUploading = ref(false)
const logoSrc = computed(() => configuration.value?.logoUrl ?? null)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const [infoResult, configResult] = await Promise.all([
    adminPlatformGetInformation(),
    adminPlatformGetConfiguration(),
  ])
  loading.value = false
  if (infoResult.error || configResult.error) {
    loadError.value = parseApiError(infoResult.error ?? configResult.error).message
    return
  }
  information.value = infoResult.data ?? null
  configuration.value = configResult.data ?? null
  name.value = configResult.data?.name ?? ''
  description.value = configResult.data?.description ?? ''
}

async function save(): Promise<void> {
  if (!configuration.value || !name.value.trim()) return
  saving.value = true
  const { data, error } = await adminPlatformUpdateConfiguration({
    body: {
      name: name.value.trim(),
      description: description.value.trim() || null,
    },
  })
  saving.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  configuration.value = data ?? configuration.value
  if (data) globalConfiguration.value = data
  toast.success(translate("平台配置已保存"))
}

async function uploadLogo(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !configuration.value) return
  logoUploading.value = true
  const { data, error } = await adminPlatformUploadLogo({
    body: { file },
  })
  logoUploading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  if (data) {
    configuration.value = data
    globalConfiguration.value = data
  }
  toast.success(translate("Logo 已更新"))
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-6 lg:grid-cols-2">
      <Skeleton class="h-56 w-full" />
      <Skeleton class="h-56 w-full" />
    </div>

    <div v-else class="grid gap-6 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>{{ $t('平台信息') }}</CardTitle>
          <CardDescription>{{ $t('后端版本与项目贡献者') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-4">
          <div class="flex items-center gap-2">
            <span class="text-sm text-muted-foreground">{{ $t('版本') }}</span>
            <Badge variant="secondary" class="font-mono">{{ information?.version ?? '-' }}</Badge>
          </div>
          <div class="flex flex-col gap-2">
            <span class="text-sm text-muted-foreground">{{ $t('贡献者') }}</span>
            <div v-if="information?.contributors?.length" class="flex flex-wrap gap-2">
              <Avatar v-for="contributor in information.contributors" :key="contributor.id" class="size-8">
                <AvatarImage v-if="contributor.avatarUrl" :src="contributor.avatarUrl" :alt="contributor.id ?? ''" />
                <AvatarFallback>{{ contributor.id?.slice(0, 2) ?? '?' }}</AvatarFallback>
              </Avatar>
            </div>
            <span v-else class="text-sm text-muted-foreground">-</span>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{{ $t('平台配置') }}</CardTitle>
          <CardDescription>{{ $t('平台名称、描述与 Logo') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="platform-name">{{ $t('平台名称') }}</FieldLabel>
                <Input id="platform-name" v-model="name" required maxlength="100" />
              </Field>
              <Field>
                <FieldLabel for="platform-description">{{ $t('平台描述') }}</FieldLabel>
                <Textarea id="platform-description" v-model="description" rows="3" maxlength="500" />
              </Field>
              <Field>
                <FieldLabel>Logo</FieldLabel>
                <div class="flex items-center gap-4">
                  <img
                    v-if="logoSrc"
                    :src="logoSrc"
                    :alt="$t('平台 Logo')"
                    class="size-16 rounded-md border object-contain"
                  >
                  <span v-else class="text-sm text-muted-foreground">{{ $t('尚未设置 Logo') }}</span>
                  <input ref="logoInput" type="file" accept="image/*" class="hidden" @change="uploadLogo">
                  <Button type="button" variant="outline" :disabled="logoUploading" @click="logoInput?.click()">
                    <Spinner v-if="logoUploading" data-icon="inline-start" />
                    <Upload v-else data-icon="inline-start" /> {{ $t('上传 Logo') }} </Button>
                </div>
              </Field>
              <Field orientation="horizontal">
                <Button type="submit" :disabled="saving || !name.trim()">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('保存配置') }} </Button>
              </Field>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
