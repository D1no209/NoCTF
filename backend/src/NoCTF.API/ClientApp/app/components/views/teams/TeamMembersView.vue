<script setup lang="ts">
import { toRefs } from 'vue'
import type { TeamMembersViewState } from '~/features/teams/useTeamMembers'

const viewProps = defineProps<{ state: TeamMembersViewState }>()
const { Crown, UserMinus, profiles, loaded, loadError, removing, loadProfiles, remove, team, canManage } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-2">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(loadError) }}</span>
        <Button type="button" size="sm" variant="outline" @click="loadProfiles">{{ $t('common.label.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <ul class="flex flex-col gap-2">
    <Skeleton v-if="!loaded" class="h-12 w-full" />
    <li
      v-for="memberId in team.memberIds ?? []"
      :key="memberId"
      class="flex min-w-0 items-center gap-3 rounded-md border px-3 py-2"
    >
      <Avatar class="size-8 shrink-0">
        <AvatarImage
          v-if="profiles[memberId]?.avatarUrl"
          :src="profiles[memberId]!.avatarUrl!"
          :alt="profiles[memberId]?.userName ?? ''"
        />
        <AvatarFallback>{{ profiles[memberId]?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
      </Avatar>
      <NuxtLink :to="`/users/${memberId}`" class="min-w-0 text-sm font-medium [overflow-wrap:anywhere] hover:underline">
        {{ profiles[memberId]?.userName ?? memberId.slice(0, 8) }}
      </NuxtLink>
      <Badge v-if="memberId === team.captainId" variant="secondary" class="shrink-0 gap-1">
        <Crown class="size-3" /> {{ $t('common.label.captain') }} </Badge>
      <Button
        v-if="canManage && memberId !== team.captainId"
        variant="ghost"
        size="sm"
        class="ml-auto"
        :disabled="removing === memberId"
        @click="remove(memberId)"
      >
        <Spinner v-if="removing === memberId" data-icon="inline-start" />
        <UserMinus v-else data-icon="inline-start" /> {{ $t('common.label.remove') }} </Button>
    </li>
    </ul>
  </div>
</template>
