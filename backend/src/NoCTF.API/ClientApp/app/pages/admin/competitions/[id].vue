<script setup lang="ts">
import { Megaphone } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCreateCompetitionAnnouncement,
  adminGetCompetition,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'
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
const canJudge = computed(() => role.value !== 'observer')
const canManagePermissions = computed(() => role.value === 'owner')
const canAnnounce = computed(() => role.value !== 'observer')

const announcementOpen = ref(false)
const announcementTitle = ref('')
const announcementBody = ref('')
const announcementAudience = ref<NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience>('Participants')
const announcementPending = ref(false)
const announcementError = ref<string | null>(null)

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
  const protocolRole = competition.value?.administrationRole
  role.value = protocolRole === 'Manager'
    ? 'manager'
    : protocolRole === 'Judge'
      ? 'judge'
      : protocolRole === 'Owner'
        ? 'owner'
        : 'observer'
}

async function publishAnnouncement() {
  if (announcementPending.value) return
  const title = announcementTitle.value.trim()
  const body = announcementBody.value.trim()
  announcementError.value = !title
    ? '请输入通知标题。'
    : !body
      ? '请输入通知内容。'
      : null
  if (announcementError.value) return

  announcementPending.value = true
  try {
    const { error: requestError } = await adminCreateCompetitionAnnouncement({
      path: { competitionId },
      body: { title, body, audience: announcementAudience.value },
    })
    if (requestError) throw requestError
    toast.success('比赛通知已发布')
    announcementOpen.value = false
    announcementTitle.value = ''
    announcementBody.value = ''
    announcementError.value = null
  }
  catch (requestError) {
    announcementError.value = parseApiError(requestError, '发布比赛通知失败').message
    toast.error(announcementError.value)
  }
  finally {
    announcementPending.value = false
  }
}

function setAnnouncementOpen(open: boolean) {
  if (announcementPending.value) return
  announcementOpen.value = open
  if (!open) announcementError.value = null
}

provide(CompetitionAdminKey, {
  competitionId,
  competition,
  role,
  canWrite,
  canJudge,
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
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-2xl font-semibold">{{ competition.title }}</h1>
          <GameModeBadge :mode="competition.mode" />
          <CompetitionStatusBadge :status="competition.status" />
          <Badge variant="outline">我的角色:{{ RoleLabel[role] }}</Badge>
        </div>
        <Button v-if="canAnnounce" variant="outline" @click="announcementOpen = true">
          <Megaphone data-icon="inline-start" />
          发布通知
        </Button>
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

      <Dialog :open="announcementOpen" @update:open="setAnnouncementOpen">
        <DialogContent class="max-h-[calc(100vh-2rem)] overflow-y-auto sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>发布比赛通知</DialogTitle>
            <DialogDescription>向全体参赛者或赛事工作人员发送一条永久通知。</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel>通知对象</FieldLabel>
              <Select v-model="announcementAudience">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="Participants">全体参赛者</SelectItem>
                    <SelectItem value="Collaborators">赛事工作人员</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="announcement-title">标题</FieldLabel>
              <Input
                id="announcement-title"
                v-model="announcementTitle"
                maxlength="160"
                @input="announcementError = null"
              />
            </Field>
            <Field>
              <FieldLabel for="announcement-body">内容</FieldLabel>
              <Textarea
                id="announcement-body"
                v-model="announcementBody"
                class="min-h-40"
                rows="8"
                maxlength="16000"
                @input="announcementError = null"
              />
            </Field>
            <p v-if="announcementError" role="alert" class="text-sm text-destructive">
              {{ announcementError }}
            </p>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" :disabled="announcementPending" @click="setAnnouncementOpen(false)">
              取消
            </Button>
            <Button :disabled="announcementPending" @click="publishAnnouncement">
              <Spinner v-if="announcementPending" data-icon="inline-start" />
              发布通知
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
  </div>
</template>
