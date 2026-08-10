<script setup lang="ts">
import { KeyRound, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformDeleteUser,
  adminPlatformGetUser,
  adminPlatformInvalidateUserTokens,
  adminPlatformListUsers,
  adminPlatformPreviewUserDeletion,
  adminPlatformUpdateUserRole,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode,
  NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse
type DeletionPreview = NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse

const { user: currentUser } = useAuth()

const users = ref<PlatformUser[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)
const search = ref('')
const roleFilter = ref('all')

const ROLE_LABELS: Record<string, string> = { User: translate("用户"), Organizer: translate("组织者"), Administrator: translate("管理员") }
const STATUS_LABELS: Record<string, string> = { Active: translate("正常"), Banned: translate("已封禁"), Disabled: translate("已禁用"), Anonymized: translate("已匿名") }
const REFERENCE_LABELS: Record<string, string> = {
  CompetitionOwner: translate("竞赛负责人"),
  CompetitionCollaborator: translate("竞赛协作者"),
  ChallengeOwner: translate("题库模板负责人"),
  ChallengeManager: translate("题库模板管理员"),
  TeamCaptain: translate("队伍队长"),
  TeamMember: translate("队伍成员"),
  Submission: translate("提交记录"),
  PatchUpload: translate("补丁上传"),
  Notification: translate("通知"),
  GameplayFact: translate("比赛事实"),
  CompetitionLifecycleAudit: translate("竞赛生命周期审计"),
  CompetitionQuestion: translate("竞赛问答"),
  CompetitionQuestionEntry: translate("问答回复"),
  CompetitionEvent: translate("竞赛事件"),
  UserAccountLifecycleAudit: translate("账户生命周期审计"),
}

const filteredUsers = computed(() => {
  const keyword = search.value.trim().toLowerCase()
  return users.value.filter((user) => {
    if (roleFilter.value !== 'all' && user.role !== roleFilter.value) return false
    if (!keyword) return true
    return (user.userName ?? '').toLowerCase().includes(keyword)
      || (user.email ?? '').toLowerCase().includes(keyword)
  })
})

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminPlatformListUsers()
  loading.value = false
  if (error) {
    loadError.value = parseApiError(error).message
    return
  }
  users.value = data?.items ?? []
}

// ---------- 详情 Sheet ----------
const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<PlatformUser | null>(null)
const pendingRole = ref('User')
const roleSaving = ref(false)
const invalidating = ref(false)

async function openDetail(user: PlatformUser): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  if (!user.id) return
  const { data, error } = await adminPlatformGetUser({ path: { userId: user.id } })
  detailLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    detailOpen.value = false
    return
  }
  detail.value = data ?? null
  pendingRole.value = data?.role ?? 'User'
}

async function saveRole(): Promise<void> {
  if (!detail.value?.id) return
  roleSaving.value = true
  const { data, error, response } = await adminPlatformUpdateUserRole({
    path: { userId: detail.value.id },
    body: { role: pendingRole.value as 'User' | 'Organizer' | 'Administrator' },
  })
  roleSaving.value = false
  if (error) {
    if (response?.status === 409) {
      toast.error(translate("该用户仍是活跃竞赛/模板的负责人或管理员,无法调整角色"))
    }
    else {
      toast.error(parseApiError(error).message)
    }
    return
  }
  detail.value = data ?? detail.value
  toast.success(translate("角色已更新"))
  await load()
}

async function invalidateTokens(): Promise<void> {
  if (!detail.value?.id) return
  invalidating.value = true
  const { error } = await adminPlatformInvalidateUserTokens({ path: { userId: detail.value.id } })
  invalidating.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success(translate("已吊销该用户的全部令牌"))
}

// ---------- 删除 ----------
const deleteOpen = ref(false)
const previewLoading = ref(false)
const preview = ref<DeletionPreview | null>(null)
const deletionMode = ref<NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode>('Anonymize')
const deletionReason = ref('')
const deleting = ref(false)

async function startDelete(): Promise<void> {
  if (!detail.value?.id) return
  deleteOpen.value = true
  previewLoading.value = true
  preview.value = null
  deletionReason.value = ''
  const { data, error } = await adminPlatformPreviewUserDeletion({ path: { userId: detail.value.id } })
  previewLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    deleteOpen.value = false
    return
  }
  preview.value = data ?? null
  deletionMode.value = data?.canAnonymize ? 'Anonymize' : 'HardDelete'
}

function deletionConflictMessage(error: unknown): string {
  const code = (error as { code?: string } | undefined)?.code
  switch (code) {
    case 'SelfDeletionForbidden':
      return translate("不能删除自己的账户")
    case 'LastAdministratorProtected':
      return translate("不能删除最后一名管理员")
    case 'HardDeleteBlocked':
      return translate("该用户存在业务引用,无法物理删除,请改用匿名化")
    case 'AlreadyAnonymized':
      return translate("该用户已被匿名化")
    default:
      return parseApiError(error).message
  }
}

async function confirmDelete(): Promise<void> {
  if (!detail.value?.id || !deletionReason.value.trim()) return
  deleting.value = true
  const { error } = await adminPlatformDeleteUser({
    path: { userId: detail.value.id },
    body: { mode: deletionMode.value, reason: deletionReason.value.trim() },
  })
  deleting.value = false
  if (error) {
    toast.error(deletionConflictMessage(error))
    return
  }
  toast.success(deletionMode.value === 'HardDelete' ? translate("用户已物理删除") : translate("用户已匿名化"))
  deleteOpen.value = false
  detailOpen.value = false
  await load()
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center gap-4">
      <Input v-model="search" class="max-w-xs" :placeholder="$t('搜索用户名或邮箱')" />
      <Select v-model="roleFilter">
        <SelectTrigger class="w-36">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="all">{{ $t('全部角色') }}</SelectItem>
            <SelectItem value="User">{{ $t('用户') }}</SelectItem>
            <SelectItem value="Organizer">{{ $t('组织者') }}</SelectItem>
            <SelectItem value="Administrator">{{ $t('管理员') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <Card v-if="loading">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 6" :key="i" class="h-10 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="filteredUsers.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('没有匹配的用户') }}</EmptyTitle>
        <EmptyDescription>{{ $t('调整搜索或筛选条件。') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('用户名') }}</TableHead>
            <TableHead>{{ $t('邮箱') }}</TableHead>
            <TableHead>{{ $t('类型') }}</TableHead>
            <TableHead>{{ $t('角色') }}</TableHead>
            <TableHead>{{ $t('状态') }}</TableHead>
            <TableHead>{{ $t('注册时间') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="user in filteredUsers"
            :key="user.id"
            class="cursor-pointer"
            @click="openDetail(user)"
          >
            <TableCell class="font-medium">
              {{ user.userName }}
              <Badge v-if="user.id === currentUser?.userId" variant="outline" class="ml-2">{{ $t('我') }}</Badge>
            </TableCell>
            <TableCell class="text-muted-foreground">{{ user.email }}</TableCell>
            <TableCell>
              <Badge :variant="user.kind === 'Bot' ? 'secondary' : 'outline'">
                {{ user.kind === 'Bot' ? 'Bot' : $t('用户') }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge :variant="user.role === 'Administrator' ? 'default' : 'secondary'">
                {{ ROLE_LABELS[String(user.role)] ?? user.role }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge :variant="user.accountStatus === 'Active' ? 'outline' : 'destructive'">
                {{ STATUS_LABELS[String(user.accountStatus)] ?? user.accountStatus }}
              </Badge>
            </TableCell>
            <TableCell>
              <AdminDateTime :value="user.createdAt" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('用户详情') }}</SheetTitle>
          <SheetDescription>{{ detail?.userName ?? $t('加载中…') }}</SheetDescription>
        </SheetHeader>
        <div v-if="detailLoading" class="flex flex-col gap-3 px-4">
          <Skeleton v-for="i in 5" :key="i" class="h-8 w-full" />
        </div>
        <div v-else-if="detail" class="flex flex-col gap-6 px-4 pb-6">
          <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <dt class="text-muted-foreground">{{ $t('用户 ID') }}</dt>
            <dd class="font-mono break-all">{{ detail.id }}</dd>
            <dt class="text-muted-foreground">{{ $t('用户名') }}</dt>
            <dd>{{ detail.userName }}</dd>
            <dt class="text-muted-foreground">{{ $t('邮箱') }}</dt>
            <dd class="break-all">
              {{ detail.email }}
              <Badge v-if="detail.emailVerified" variant="secondary" class="ml-1">{{ $t('已验证') }}</Badge>
              <Badge v-else variant="outline" class="ml-1">{{ $t('未验证') }}</Badge>
            </dd>
            <dt class="text-muted-foreground">{{ $t('类型') }}</dt>
            <dd>{{ detail.kind === 'Bot' ? 'Bot' : $t('用户') }}</dd>
            <dt class="text-muted-foreground">{{ $t('状态') }}</dt>
            <dd>{{ STATUS_LABELS[String(detail.accountStatus)] ?? detail.accountStatus }}</dd>
            <dt class="text-muted-foreground">{{ $t('令牌版本') }}</dt>
            <dd class="font-mono tabular-nums">{{ detail.tokenVersion ?? 0 }}</dd>
            <dt class="text-muted-foreground">{{ $t('注册时间') }}</dt>
            <dd><AdminDateTime :value="detail.createdAt" /></dd>
            <dt class="text-muted-foreground">{{ $t('更新时间') }}</dt>
            <dd><AdminDateTime :value="detail.updatedAt" /></dd>
          </dl>

          <Separator />

          <FieldGroup>
            <Field>
              <FieldLabel for="user-role">{{ $t('平台角色') }}</FieldLabel>
              <div class="flex items-center gap-2">
                <Select v-model="pendingRole" :disabled="detail.id === currentUser?.userId">
                  <SelectTrigger id="user-role" class="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="User">{{ $t('用户') }}</SelectItem>
                      <SelectItem value="Organizer">{{ $t('组织者') }}</SelectItem>
                      <SelectItem value="Administrator">{{ $t('管理员') }}</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <Button
                  :disabled="roleSaving || pendingRole === String(detail.role ?? 'User') || detail.id === currentUser?.userId"
                  @click="saveRole"
                >
                  <Spinner v-if="roleSaving" data-icon="inline-start" /> {{ $t('保存') }} </Button>
              </div>
              <FieldDescription v-if="detail.id === currentUser?.userId">{{ $t('不能修改自己的角色。') }}</FieldDescription>
            </Field>
          </FieldGroup>

          <Separator />

          <div class="flex flex-col gap-3">
            <h3 class="text-sm font-medium">{{ $t('危险操作') }}</h3>
            <div class="flex flex-wrap gap-2">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button variant="outline">
                    <KeyRound data-icon="inline-start" /> {{ $t('吊销全部令牌') }} </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{ $t('吊销令牌') }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{ $t('将吊销用户「{user}」的全部访问与刷新令牌，该用户需要重新登录。', { user: detail.userName ?? '-' }) }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
                    <AlertDialogAction :disabled="invalidating" @click="invalidateTokens">
                      <Spinner v-if="invalidating" data-icon="inline-start" /> {{ $t('确认吊销') }} </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
              <Button variant="destructive" @click="startDelete">
                <Trash2 data-icon="inline-start" /> {{ $t('删除用户') }} </Button>
            </div>
          </div>
        </div>
      </SheetContent>
    </Sheet>

    <AlertDialog v-model:open="deleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('删除用户「{user}」', { user: detail?.userName ?? '-' }) }}</AlertDialogTitle>
          <AlertDialogDescription> {{ $t('删除不可恢复,请先确认影响范围。') }} </AlertDialogDescription>
        </AlertDialogHeader>
        <div v-if="previewLoading" class="flex flex-col gap-2">
          <Skeleton v-for="i in 3" :key="i" class="h-8 w-full" />
        </div>
        <div v-else-if="preview" class="flex flex-col gap-4">
          <Alert v-if="preview.selfDeletionForbidden || preview.lastAdministratorProtected" variant="destructive">
            <AlertDescription>
              {{ preview.selfDeletionForbidden ? $t('不能删除自己的账户。') : $t('不能删除最后一名管理员。') }}
            </AlertDescription>
          </Alert>
          <div v-if="preview.references?.length" class="flex flex-col gap-2">
            <p class="text-sm text-muted-foreground">{{ $t('该用户存在以下业务引用:') }}</p>
            <div class="flex flex-wrap gap-2">
              <Badge v-for="reference in preview.references" :key="reference.code" variant="secondary">
                {{ REFERENCE_LABELS[reference.code ?? ''] ?? reference.code }} × {{ reference.count }}
              </Badge>
            </div>
          </div>
          <p v-else class="text-sm text-muted-foreground">{{ $t('该用户没有业务引用。') }}</p>
          <FieldGroup>
            <Field>
              <FieldLabel for="deletion-mode">{{ $t('删除方式') }}</FieldLabel>
              <Select v-model="deletionMode">
                <SelectTrigger id="deletion-mode" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem v-if="preview.canAnonymize" value="Anonymize">{{ $t('匿名化(保留数据,移除身份)') }}</SelectItem>
                    <SelectItem v-if="preview.canHardDelete" value="HardDelete">{{ $t('物理删除(彻底清除)') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="deletion-reason">{{ $t('删除原因') }}</FieldLabel>
              <Input id="deletion-reason" v-model="deletionReason" required :placeholder="$t('将记录到审计日志')" />
            </Field>
          </FieldGroup>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            :disabled="deleting || !preview || !deletionReason.trim()
              || preview.selfDeletionForbidden || preview.lastAdministratorProtected"
            @click="confirmDelete"
          >
            <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('确认删除') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
