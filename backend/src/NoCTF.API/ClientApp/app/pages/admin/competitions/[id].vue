<script setup lang="ts">
import { Container, Download, FileCheck, GitBranch, KeyRound, LayoutDashboard, Megaphone, Puzzle, Settings, ShieldAlert, Trophy, Users } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCreateCompetitionAnnouncement,
  adminGetCompetition,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'
import { CompetitionAdminKey } from '~/lib/admin-competition'
import type { CompetitionAdminRole } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const competitionId = route.params.id as string
const { user, isAdministrator } = useAuth()

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const role = ref<CompetitionAdminRole>('Observer')
const loading = ref(true)
const error = ref<string | null>(null)

const canWrite = computed(() => role.value === 'Owner' || role.value === 'Manager')
const canJudge = computed(() => role.value !== 'Observer')
const canManagePermissions = computed(() => role.value === 'Owner')
const canAnnounce = computed(() => role.value !== 'Observer')

const announcementOpen = ref(false)
const announcementTitle = ref('')
const announcementBody = ref('')
const announcementAudience = ref<NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience>('Participants')
const announcementPending = ref(false)
const announcementError = ref<string | null>(null)

const RoleLabel: Record<CompetitionAdminRole, string> = {
  Owner: '负责人',
  Manager: '管理员',
  Judge: '裁判',
  Observer: '观察员',
}

async function refresh() {
  const { data, error: e } = await adminGetCompetition({ path: { competitionId } })
  if (e || !data) {
    error.value = parseApiError(e, translate("加载竞赛失败")).message
    return
  }
  competition.value = data
  error.value = null
}

async function resolveRole() {
  if (isAdministrator.value || (user.value?.userId && competition.value?.ownerId === user.value.userId)) {
    role.value = 'Owner'
    return
  }
  const protocolRole = competition.value?.administrationRole
  role.value = protocolRole ?? 'Observer'
}

async function publishAnnouncement() {
  if (announcementPending.value) return
  const title = announcementTitle.value.trim()
  const body = announcementBody.value.trim()
  announcementError.value = !title
    ? translate("请输入通知标题。")
    : !body
      ? translate("请输入通知内容。")
      : null
  if (announcementError.value) return

  announcementPending.value = true
  try {
    const { error: requestError } = await adminCreateCompetitionAnnouncement({
      path: { competitionId },
      body: { title, body, audience: announcementAudience.value },
    })
    if (requestError) throw requestError
    toast.success(translate("比赛通知已发布"))
    announcementOpen.value = false
    announcementTitle.value = ''
    announcementBody.value = ''
    announcementError.value = null
  }
  catch (requestError) {
    announcementError.value = parseApiError(requestError, translate("发布比赛通知失败")).message
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

const base = `/admin/competitions/${competitionId}`

const navGroups = computed<WorkspaceNavGroup[]>(() => [
  {
    label: translate("运营"),
    items: [
      { to: base, label: translate("概览"), icon: LayoutDashboard, exact: true },
      { to: `${base}/configuration`, label: translate("配置"), icon: Settings },
      { to: `${base}/tracks`, label: translate("赛道"), icon: GitBranch },
      { to: `${base}/challenges`, label: translate("题目"), icon: Puzzle },
      { to: `${base}/teams`, label: translate("团队"), icon: Users },
    ],
  },
  {
    label: translate("监控"),
    items: [
      { to: `${base}/submissions`, label: translate("提交"), icon: FileCheck },
      { to: `${base}/runtimes`, label: translate("运行时"), icon: Container },
      { to: `${base}/cheats`, label: translate("作弊"), icon: ShieldAlert },
      { to: `${base}/leaderboard`, label: translate("记分板"), icon: Trophy },
    ],
  },
  {
    label: translate("管理"),
    items: [
      { to: `${base}/exports`, label: translate("导出"), icon: Download },
      ...(canManagePermissions.value
        ? [{ to: `${base}/permissions`, label: translate("权限"), icon: KeyRound }]
        : []),
    ],
  },
])

onMounted(async () => {
  loading.value = true
  await refresh()
  if (competition.value) await resolveRole()
  loading.value = false
})
</script>

<template>
  <AppWorkspaceNav v-if="competition" :groups="navGroups" :title="competition.title">
    <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-8 md:px-6">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-display text-2xl">{{ competition.title }}</h1>
          <GameModeBadge :mode="competition.mode" />
          <CompetitionStatusBadge :status="competition.status" />
          <Badge variant="outline">{{ $t('我的角色：{role}', { role: $t(RoleLabel[role]) }) }}</Badge>
        </div>
        <Button v-if="canAnnounce" variant="outline" @click="announcementOpen = true">
          <Megaphone data-icon="inline-start" /> {{ $t('发布通知') }} </Button>
      </div>
      <NuxtPage />
    </div>

    <Dialog :open="announcementOpen" @update:open="setAnnouncementOpen">
        <DialogContent class="max-h-[calc(100vh-2rem)] overflow-y-auto sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{{ $t('发布比赛通知') }}</DialogTitle>
            <DialogDescription>{{ $t('向全体参赛者或赛事工作人员发送一条永久通知。') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel>{{ $t('通知对象') }}</FieldLabel>
              <Select v-model="announcementAudience">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="Participants">{{ $t('全体参赛者') }}</SelectItem>
                    <SelectItem value="Collaborators">{{ $t('赛事工作人员') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="announcement-title">{{ $t('标题') }}</FieldLabel>
              <Input
                id="announcement-title"
                v-model="announcementTitle"
                maxlength="160"
                @input="announcementError = null"
              />
            </Field>
            <Field>
              <FieldLabel for="announcement-body">{{ $t('内容') }}</FieldLabel>
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
            <Button variant="outline" :disabled="announcementPending" @click="setAnnouncementOpen(false)"> {{ $t('取消') }} </Button>
            <Button :disabled="announcementPending" @click="publishAnnouncement">
              <Spinner v-if="announcementPending" data-icon="inline-start" /> {{ $t('发布通知') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
  </AppWorkspaceNav>

  <div v-else class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div v-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-10 w-64" />
      <Skeleton class="h-8 w-full max-w-xl" />
      <Skeleton class="h-64 w-full" />
    </div>
    <Alert v-else variant="destructive">
      <AlertDescription>{{ error ?? $t('加载竞赛失败') }}</AlertDescription>
    </Alert>
  </div>
</template>
