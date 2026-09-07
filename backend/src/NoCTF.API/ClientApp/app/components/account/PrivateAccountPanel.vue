<script setup lang="ts">
import { LockKeyhole } from '@lucide/vue'
import { adminGetPrivatePlatformUser, adminGetPrivateTeamMember } from '~/api'
import type { NoCtfapiEndpointsAdministrationPlatformPrivateAccountResponse } from '~/api'

const props = defineProps<{ userId: string, competitionId?: string, teamId?: string }>()
const data = ref<NoCtfapiEndpointsAdministrationPlatformPrivateAccountResponse | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
let revision = 0
const kinds: Record<string, string> = { Registered: '注册成功', LoggedIn: '登录成功', LoginFailed: '登录失败', FlagSubmitted: 'Flag 提交', PatchUploaded: 'Patch 上传' }
async function load() {
  const ticket = ++revision
  data.value = null
  loading.value = true
  error.value = null
  try {
    const result = props.competitionId && props.teamId
      ? await adminGetPrivateTeamMember({ path: { competitionId: props.competitionId, teamId: props.teamId, userId: props.userId } })
      : await adminGetPrivatePlatformUser({ path: { userId: props.userId } })
    if (ticket !== revision) return
    if (result.error) throw result.error
    data.value = result.data ?? null
  }
  catch (e) { if (ticket === revision) error.value = parseApiError(e).message }
  finally { if (ticket === revision) loading.value = false }
}
watch(() => [props.userId, props.competitionId, props.teamId], load, { immediate: true })
onBeforeUnmount(() => revision++)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-4">
    <h3 class="flex items-center gap-2 font-semibold"><LockKeyhole class="size-4" />{{ $t('校级比赛信息') }}</h3>
    <p class="text-xs text-muted-foreground">{{ $t('用户自行填写，未经实名认证。仅限赛事核验和安全排查使用。') }}</p>
    <Skeleton v-if="loading" class="h-24 w-full" />
    <Alert v-else-if="error" variant="destructive"><AlertDescription>{{ error }}<Button variant="outline" size="sm" @click="load">{{ $t('重试') }}</Button></AlertDescription></Alert>
    <template v-else-if="data">
      <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
        <dt class="text-muted-foreground">{{ $t('姓名') }}</dt><dd class="break-all">{{ data.identity?.fullName || $t('未填写') }}</dd>
        <dt class="text-muted-foreground">{{ $t('学号') }}</dt><dd class="break-all font-mono">{{ data.identity?.studentNumber || $t('未填写') }}</dd>
      </dl>
      <Separator />
      <h4 class="font-medium">{{ $t('来源 IP 记录') }}</h4>
      <p class="text-xs text-muted-foreground">{{ $t('仅展示 {days} 天内最近 50 条记录。IP 相同不能作为作弊结论。', { days: data.retentionDays ?? 30 }) }}</p>
      <p v-if="competitionId" class="text-xs text-muted-foreground">{{ $t('仅包含本场比赛的提交，不包含登录历史。') }}</p>
      <Empty v-if="!data.activities?.length"><EmptyDescription>{{ $t('暂无留存记录') }}</EmptyDescription></Empty>
      <ol v-else class="flex flex-col divide-y">
        <li v-for="item in data.activities" :key="item.id" class="flex min-w-0 flex-col gap-1 py-3 text-sm">
          <div class="flex flex-wrap justify-between gap-2"><span>{{ $t(kinds[item.kind ?? ''] ?? item.kind ?? '') }}</span><time class="font-mono text-xs text-muted-foreground">{{ formatDateTime(item.occurredAt) }}</time></div>
          <span class="break-all font-mono">{{ item.ipAddress || $t('来源未知') }}</span>
          <span v-if="item.gameplayFactId" class="break-all font-mono text-xs text-muted-foreground">{{ $t('提交 ID') }}: {{ item.gameplayFactId }}</span>
          <span v-if="!competitionId && item.competitionId" class="break-all font-mono text-xs text-muted-foreground">{{ $t('比赛 ID') }}: {{ item.competitionId }}</span>
        </li>
      </ol>
    </template>
  </section>
</template>
