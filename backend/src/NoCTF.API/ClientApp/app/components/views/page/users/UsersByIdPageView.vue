<script setup lang="ts">
import { toRefs } from 'vue'
import type { UsersByIdPageViewState } from '~/features/routes/users/useUsersByIdPage'

const viewProps = defineProps<{ state: UsersByIdPageViewState }>()
const { profile, loading, error } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex w-full max-w-5xl flex-col items-center px-4 py-12 md:items-end md:px-6">
    <Alert v-if="error" variant="destructive" class="w-full max-w-2xl">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <Skeleton v-else-if="loading" class="h-48 w-full max-w-2xl" />

    <Card v-else-if="profile" data-public-profile-card class="relative mt-12 w-full max-w-2xl overflow-visible pt-12">
      <Avatar data-public-profile-avatar class="absolute top-0 left-1/2 z-10 size-24 -translate-x-1/2 -translate-y-1/2">
            <AvatarImage v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.userName ?? ''" />
            <AvatarFallback>{{ profile.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
      </Avatar>
      <CardHeader class="items-center text-center">
        <CardTitle class="text-display text-xl">{{ profile.userName }}</CardTitle>
      </CardHeader>
      <CardContent class="text-center">
        <p v-if="profile.description" class="whitespace-pre-line text-sm leading-6">
          {{ profile.description }}
        </p>
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.thisUserHasNotFilledOutAProfileYet') }}</p>
      </CardContent>
    </Card>
  </div>
</template>
