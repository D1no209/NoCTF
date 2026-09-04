<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Crown, UserMinus } from '@lucide/vue'
import { removeTeamMemberEndpoint, userProfileGet } from '~/api'
import type {
  NoCtfapiEndpointsAuthenticationPublicUserProfileResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'

const props = defineProps<{
  competitionId: string
  team: NoCtfapiEndpointsTeamsTeamResponse
  /** 队长视角:可移除成员 */
  canManage?: boolean
}>()

const emit = defineEmits<{ changed: [] }>()

const profiles = ref<Record<string, NoCtfapiEndpointsAuthenticationPublicUserProfileResponse>>({})
const loaded = ref(false)
const loadError = ref<string | null>(null)
const removing = ref<string | null>(null)

async function loadProfiles() {
  const ids = props.team.memberIds ?? []
  loaded.value = false
  loadError.value = null
  const entries = await Promise.all(
    ids.map(async (id) => {
      const { data, error } = await userProfileGet({ path: { userId: id } })
      return { id, data, error }
    }),
  )
  profiles.value = Object.fromEntries(
    entries
      .filter(entry => !!entry.data)
      .map(entry => [entry.id, entry.data!] as const),
  )
  const failures = entries.filter(entry => entry.error || !entry.data).length
  if (failures > 0) {
    loadError.value = translate('有 {count} 名队员资料加载失败，当前暂时显示用户标识。', {
      count: failures,
    })
  }
  loaded.value = true
}

onMounted(loadProfiles)

async function remove(userId: string) {
  removing.value = userId
  const { error } = await removeTeamMemberEndpoint({
    path: { competitionId: props.competitionId, teamId: props.team.id!, userId },
  })
  removing.value = null
  if (error) {
    toast.error(parseApiError(error, translate("移除成员失败")).message)
    return
  }
  toast.success(translate("成员已移除"))
  emit('changed')
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ loadError }}</span>
        <Button type="button" size="sm" variant="outline" @click="loadProfiles">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <ul class="flex flex-col gap-2">
    <Skeleton v-if="!loaded" class="h-12 w-full" />
    <li
      v-for="memberId in team.memberIds ?? []"
      :key="memberId"
      class="flex items-center gap-3 rounded-md border px-3 py-2"
    >
      <Avatar class="size-8">
        <AvatarImage
          v-if="profiles[memberId]?.avatarUrl"
          :src="profiles[memberId]!.avatarUrl!"
          :alt="profiles[memberId]?.userName ?? ''"
        />
        <AvatarFallback>{{ profiles[memberId]?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
      </Avatar>
      <NuxtLink :to="`/users/${memberId}`" class="text-sm font-medium hover:underline">
        {{ profiles[memberId]?.userName ?? memberId.slice(0, 8) }}
      </NuxtLink>
      <Badge v-if="memberId === team.captainId" variant="secondary" class="gap-1">
        <Crown class="size-3" /> {{ $t('队长') }} </Badge>
      <Button
        v-if="canManage && memberId !== team.captainId"
        variant="ghost"
        size="sm"
        class="ml-auto"
        :disabled="removing === memberId"
        @click="remove(memberId)"
      >
        <Spinner v-if="removing === memberId" data-icon="inline-start" />
        <UserMinus v-else data-icon="inline-start" /> {{ $t('移除') }} </Button>
    </li>
    </ul>
  </div>
</template>
