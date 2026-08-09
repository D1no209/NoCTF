<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { adminListCheatIncidents, adminListCompetitions } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import type { CompetitionAdminRole } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const { user, isAdministrator, canOrganize } = useAuth()

const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])
const roles = ref<Record<string, CompetitionAdminRole>>({})
const loading = ref(true)
const error = ref<string | null>(null)
const includeDeleted = ref(route.query.includeDeleted === 'true')

const RoleLabel: Record<CompetitionAdminRole, string> = {
  owner: '负责人',
  manager: '管理员',
  judge: '裁判',
  observer: '观察员',
}

async function probeRole(competition: NoCtfapiEndpointsCompetitionsCompetitionResponse): Promise<CompetitionAdminRole> {
  if (isAdministrator.value || (user.value?.userId && competition.ownerId === user.value.userId))
    return 'owner'
  // Capability probe: the cheat-incident list reports judge/moderate capabilities.
  const { data } = await adminListCheatIncidents({
    path: { competitionId: competition.id! },
    query: { from: '1970-01-01T00:00:00Z', to: '2999-12-31T23:59:59Z', limit: 1 },
  }).catch(() => ({ data: undefined }))
  if (data?.canConfirm) return 'manager'
  if (data?.canDismiss) return 'judge'
  return 'observer'
}

async function load() {
  loading.value = true
  error.value = null
  const { data, error: e } = await adminListCompetitions({
    query: { includeDeleted: includeDeleted.value },
  })
  if (e || !data) {
    error.value = parseApiError(e).message
    loading.value = false
    return
  }
  items.value = data.items ?? []
  loading.value = false
  const entries = await Promise.all(
    items.value.map(async c => [c.id!, await probeRole(c)] as const),
  )
  roles.value = Object.fromEntries(entries)
}

watch(includeDeleted, () => {
  void load()
})

onMounted(load)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold">竞赛管理</h1>
        <p class="text-sm text-muted-foreground">我参与管理的全部竞赛</p>
      </div>
      <div class="flex items-center gap-3">
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          <Checkbox v-model="includeDeleted" />
          包含已删除
        </label>
        <Button v-if="canOrganize" as-child>
          <NuxtLink to="/admin/competitions/new">
            <Plus data-icon="inline-start" />
            新建竞赛
          </NuxtLink>
        </Button>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="flex flex-col gap-3">
      <Skeleton v-for="i in 3" :key="i" class="h-24 w-full" />
    </div>

    <Empty v-else-if="items.length === 0">
      <EmptyHeader>
        <EmptyTitle>暂无竞赛</EmptyTitle>
        <EmptyDescription>你还没有参与管理任何竞赛</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-else class="grid gap-4 md:grid-cols-2">
      <Card v-for="c in items" :key="c.id">
        <CardHeader>
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="truncate">{{ c.title }}</CardTitle>
            <div class="flex shrink-0 items-center gap-1">
              <GameModeBadge :mode="c.mode" />
              <CompetitionStatusBadge :status="c.status" />
              <Badge v-if="c.deletedAt" variant="destructive">已删除</Badge>
            </div>
          </div>
          <CardDescription class="line-clamp-2">{{ c.description || '暂无描述' }}</CardDescription>
        </CardHeader>
        <CardContent class="flex items-center justify-between text-sm text-muted-foreground">
          <span>{{ adminFormatDateTime(c.startTime) }} ~ {{ adminFormatDateTime(c.endTime) }}</span>
          <Badge variant="outline">{{ c.id && roles[c.id] ? RoleLabel[roles[c.id]!] : '…' }}</Badge>
        </CardContent>
        <CardFooter>
          <Button variant="outline" size="sm" as-child class="w-full">
            <NuxtLink :to="`/admin/competitions/${c.id}`">进入管理</NuxtLink>
          </Button>
        </CardFooter>
      </Card>
    </div>
  </div>
</template>
