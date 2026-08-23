<script setup lang="ts">
import { userProfileGet } from '~/api'
import type { NoCtfapiEndpointsAuthenticationPublicUserProfileResponse } from '~/api'

const route = useRoute()
const userId = route.params.id as string

const profile = ref<NoCtfapiEndpointsAuthenticationPublicUserProfileResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data, error: err } = await userProfileGet({ path: { userId } })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("用户不存在或加载失败")).message
    return
  }
  profile.value = data
})
</script>

<template>
  <div class="mx-auto flex max-w-xl flex-col px-4 py-12">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <Skeleton v-else-if="loading" class="h-48 w-full" />

    <Card v-else-if="profile">
      <CardHeader>
        <div class="flex items-center gap-4">
          <Avatar class="size-16">
            <AvatarImage v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.userName ?? ''" />
            <AvatarFallback>{{ profile.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
          </Avatar>
          <div class="flex flex-col gap-1">
            <CardTitle class="text-display text-xl">{{ profile.userName }}</CardTitle>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <p v-if="profile.description" class="whitespace-pre-line text-sm leading-6">
          {{ profile.description }}
        </p>
        <p v-else class="text-sm text-muted-foreground">{{ $t('这个用户还没有填写简介。') }}</p>
      </CardContent>
    </Card>
  </div>
</template>
