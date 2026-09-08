<script setup lang="ts">
import { toRefs } from 'vue'
import type { UsersByIdPageViewState } from '~/features/routes/users/useUsersByIdPage'

const viewProps = defineProps<{ state: UsersByIdPageViewState }>()
const { profile, loading, error } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-xl flex-col px-4 py-12">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
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
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.thisUserHasNotFilledOutAProfileYet') }}</p>
      </CardContent>
    </Card>
  </div>
</template>
