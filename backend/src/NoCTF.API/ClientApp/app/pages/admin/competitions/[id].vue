<script setup lang="ts">
import { adminGetCompetition, adminListCheatIncidents } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { CompetitionAdminKey } from '~/lib/admin-competition'
import type { CompetitionAdminRole } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const competitionId = route.params.id as string
const { user, isAdministrator } = useAuth()

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const role = ref<CompetitionAdminRole>('observer')
const loading = ref(true)
const error = ref<string | null>(null)

const canWrite = computed(() => role.value === 'owner' || role.value === 'manager')
const canManagePermissions = computed(() => role.value === 'owner')

const RoleLabel: Record<CompetitionAdminRole, string> = {
  owner: '负责人',
  manager: '管理员',
  judge: '裁判',
  observer: '观察员',
}

async function refresh() {
  const { data, error: e } = await adminGetCompetition({ path: { competitionId } })
  if (e || !data) {
    error.value = parseApiError(e, '加载竞赛失败').message
    return
  }
  competition.value = data
  error.value = null
}

async function resolveRole() {
  if (isAdministrator.value || (user.value?.userId && competition.value?.ownerId === user.value.userId)) {
    role.value = 'owner'
    return
  }
  const { data } = await adminListCheatIncidents({
    path: { competitionId },
    query: { from: '1970-01-01T00:00:00Z', to: '2999-12-31T23:59:59Z', limit: 1 },
  }).catch(() => ({ data: undefined }))
  if (data?.canConfirm) role.value = 'manager'
  else if (data?.canDismiss) role.value = 'judge'
  else role.value = 'observer'
}

provide(CompetitionAdminKey, {
  competitionId,
  competition,
  role,
  canWrite,
  canManagePermissions,
  refresh,
})

interface NavItem {
  to: string
  label: string
  ownerOnly?: boolean
}

const base = `/admin/competitions/${competitionId}`
const navItems: NavItem[] = [
  { to: base, label: '概览' },
  { to: `${base}/configuration`, label: '配置' },
  { to: `${base}/challenges`, label: '题目' },
  { to: `${base}/teams`, label: '团队' },
  { to: `${base}/submissions`, label: '提交' },
  { to: `${base}/runtimes`, label: '运行时' },
  { to: `${base}/cheats`, label: '作弊' },
  { to: `${base}/leaderboard`, label: '记分板' },
  { to: `${base}/exports`, label: '导出' },
  { to: `${base}/permissions`, label: '权限', ownerOnly: true },
]

const visibleNav = computed(() => navItems.filter(i => !i.ownerOnly || canManagePermissions.value))

// Keep deep child routes (e.g. challenge detail) highlighting their section tab.
const activeTab = computed(() => {
  const path = route.path
  const match = [...visibleNav.value]
    .sort((a, b) => b.to.length - a.to.length)
    .find(i => (i.to === base ? path === base : path.startsWith(i.to)))
  return match?.to ?? base
})

onMounted(async () => {
  loading.value = true
  await refresh()
  if (competition.value) await resolveRole()
  loading.value = false
})
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div v-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-10 w-64" />
      <Skeleton class="h-8 w-full max-w-xl" />
      <Skeleton class="h-64 w-full" />
    </div>
    <Alert v-else-if="error && !competition" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <template v-else-if="competition">
      <div class="flex flex-wrap items-center gap-3">
        <h1 class="text-2xl font-semibold">{{ competition.title }}</h1>
        <GameModeBadge :mode="competition.mode" />
        <CompetitionStatusBadge :status="competition.status" />
        <Badge variant="outline">我的角色:{{ RoleLabel[role] }}</Badge>
      </div>
      <Tabs :model-value="activeTab" @update:model-value="(v) => navigateTo(String(v))">
        <TabsList class="max-w-full justify-start gap-1 overflow-x-auto overflow-y-hidden p-1 group-data-horizontal/tabs:h-11">
          <TabsTrigger
            v-for="item in visibleNav"
            :key="item.to"
            :value="item.to"
            class="h-9 flex-none px-3.5 text-base"
          >
            {{ item.label }}
          </TabsTrigger>
        </TabsList>
      </Tabs>
      <NuxtPage />
    </template>
  </div>
</template>
