<script setup lang="ts">
import type { SortingState } from '@tanstack/vue-table'
import type { NoCtfDomainIdentityUserRole } from '@/api/generated/types.gen'
import type {
  IssuedBotToken,
  PlatformRoleAssignmentBlockers,
  PlatformUser,
  PlatformUserDeletionMode,
  PlatformUserDeletionPreview,
} from '@/api/noctf'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  createColumnHelper,
  FlexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useVueTable,
} from '@tanstack/vue-table'
import {
  Bot as BotIcon,
  Check,
  Copy,
  KeyRound,
  Loader2,
  MoreHorizontal,
  Plus,
  Search,
  ShieldAlert,
  ShieldOff,
  Trash2,
  TriangleAlert,
  UserCog,
  User as UserIcon,
} from 'lucide-vue-next'
import { computed, h, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import {
  platformAdminApi,
  readPlatformRoleAssignmentBlockers,
  readPlatformUserDeletionConflict,
} from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import {
  BOT_TOKEN_LIFETIMES,
  deletionReferenceLabelKey,
  isBot,
  isHuman,
  isValidBotUserName,
  PLATFORM_USER_ACCOUNT_STATUS,
  PLATFORM_USER_ROLE,
  userAccountStatusLabelKey,
  userKindLabelKey,
  userRoleLabelKey,
} from './platformUserPresentation'

interface VisibleIssuedBotToken extends IssuedBotToken {
  userName: string
}

const { locale, t } = useI18n()
const queryClient = useQueryClient()

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const selectedUser = ref<PlatformUser | null>(null)

const createBotDialog = ref(false)
const newBotUserName = ref('')
const newBotRole = ref(String(PLATFORM_USER_ROLE.user))

const roleDialog = ref(false)
const newRole = ref(String(PLATFORM_USER_ROLE.user))
const roleAssignmentBlockers = ref<PlatformRoleAssignmentBlockers | null>(null)

const issueTokenDialog = ref(false)
const tokenLifetime = ref(String(BOT_TOKEN_LIFETIMES[BOT_TOKEN_LIFETIMES.length - 1].seconds))
const issuedToken = ref<VisibleIssuedBotToken | null>(null)
const tokenCopied = ref(false)

const invalidateTokensDialog = ref(false)

const deletionDialog = ref(false)
const deletionPreview = ref<PlatformUserDeletionPreview | null>(null)
const deletionPreviewLoading = ref(false)
const deletionPreviewError = ref(false)
const deletionReason = ref('')
const deletionConflictCode = ref<string | null>(null)

const canCreateBot = computed(() => isValidBotUserName(newBotUserName.value))
const hasRoleChange = computed(() => {
  const currentRole = selectedUser.value?.role ?? PLATFORM_USER_ROLE.user
  return Number(newRole.value) !== currentRole
})
const hasValidDeletionReason = computed(() => {
  const length = deletionReason.value.trim().length
  return length >= 3 && length <= 500
})

const {
  data: users,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.adminUsers,
  queryFn: platformAdminApi.users,
})

const createBotMutation = useMutation({
  mutationFn: () =>
    platformAdminApi.createBot(
      newBotUserName.value.trim(),
      Number(newBotRole.value) as NoCtfDomainIdentityUserRole,
    ),
  onSuccess: (user) => {
    createBotDialog.value = false
    newBotUserName.value = ''
    replaceCachedUser(user)
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    toast.success(t('admin.users.botCreated'))
    openIssueTokenDialog(user)
  },
  onError: () => toast.error(t('admin.users.actionErrorCreateBot')),
})

const changeRoleMutation = useMutation({
  mutationFn: ({ id, role }: { id: string, role: NoCtfDomainIdentityUserRole }) =>
    platformAdminApi.updateUserRole(id, role),
  onSuccess: (user) => {
    roleDialog.value = false
    roleAssignmentBlockers.value = null
    replaceCachedUser(user)
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    toast.success(t('admin.users.roleUpdateSuccess'))
  },
  onError: async (error) => {
    roleAssignmentBlockers.value = readPlatformRoleAssignmentBlockers(error)
    await queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    toast.error(
      roleAssignmentBlockers.value
        ? t('admin.users.roleDowngradeBlocked')
        : t('admin.users.actionErrorRole'),
    )
  },
})

const issueTokenMutation = useMutation({
  mutationFn: async ({
    id,
    expiresInSeconds,
    userName,
  }: {
    id: string
    expiresInSeconds: number
    userName: string
  }) => {
    const token = await platformAdminApi.issueBotToken(id, expiresInSeconds)
    issuedToken.value = { ...token, userName }
  },
  onSuccess: () => {
    issueTokenDialog.value = false
    tokenCopied.value = false
    toast.success(t('admin.users.tokenIssued'))
  },
  onError: () => toast.error(t('admin.users.actionErrorIssueToken')),
})

const invalidateTokensMutation = useMutation({
  mutationFn: (id: string) => platformAdminApi.invalidateUserTokens(id),
  onSuccess: (user) => {
    invalidateTokensDialog.value = false
    replaceCachedUser(user)
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    toast.success(t('admin.users.tokensInvalidated'))
  },
  onError: () => toast.error(t('admin.users.actionErrorInvalidateTokens')),
})

const deleteUserMutation = useMutation({
  mutationFn: ({
    id,
    mode,
    reason,
  }: {
    id: string
    mode: PlatformUserDeletionMode
    reason: string
  }) => platformAdminApi.deleteUser(id, mode, reason),
  onSuccess: async (outcome) => {
    deletionDialog.value = false
    await queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    toast.success(
      outcome === 'PhysicallyDeleted'
        ? t('admin.users.physicalDeleteSuccess')
        : t('admin.users.anonymizeSuccess'),
    )
  },
  onError: (error) => {
    const conflict = readPlatformUserDeletionConflict(error)
    deletionConflictCode.value = conflict?.code ?? null
    if (conflict?.preview)
      deletionPreview.value = conflict.preview
    toast.error(t('admin.users.deleteError'))
  },
})

function openCreateBotDialog() {
  newBotUserName.value = ''
  newBotRole.value = String(PLATFORM_USER_ROLE.user)
  createBotDialog.value = true
}

function replaceCachedUser(user: PlatformUser) {
  queryClient.setQueryData<PlatformUser[]>(queryKeys.adminUsers, (current) => {
    if (!current)
      return [user]

    const index = current.findIndex(candidate => candidate.id === user.id)
    if (index < 0)
      return [...current, user]

    return current.map((candidate, candidateIndex) => (candidateIndex === index ? user : candidate))
  })
}

function openRoleDialog(user: PlatformUser) {
  if (!isHuman(user))
    return

  selectedUser.value = user
  newRole.value = String(user.role ?? PLATFORM_USER_ROLE.user)
  roleAssignmentBlockers.value = null
  roleDialog.value = true
}

function openIssueTokenDialog(user: PlatformUser) {
  if (!isBot(user))
    return

  selectedUser.value = user
  tokenLifetime.value = String(BOT_TOKEN_LIFETIMES[BOT_TOKEN_LIFETIMES.length - 1].seconds)
  issueTokenDialog.value = true
}

function openInvalidateTokensDialog(user: PlatformUser) {
  selectedUser.value = user
  invalidateTokensDialog.value = true
}

async function loadDeletionPreview() {
  const userId = selectedUser.value?.id
  if (!userId)
    return

  deletionPreviewLoading.value = true
  deletionPreviewError.value = false
  deletionConflictCode.value = null
  try {
    deletionPreview.value = await platformAdminApi.previewUserDeletion(userId)
  }
  catch {
    deletionPreviewError.value = true
    toast.error(t('admin.users.deletionPreviewError'))
  }
  finally {
    deletionPreviewLoading.value = false
  }
}

function openDeletionDialog(user: PlatformUser) {
  selectedUser.value = user
  deletionPreview.value = null
  deletionReason.value = ''
  deletionPreviewError.value = false
  deletionConflictCode.value = null
  deletionDialog.value = true
  void loadDeletionPreview()
}

function deleteUser(mode: PlatformUserDeletionMode) {
  const userId = selectedUser.value?.id
  if (!userId || !hasValidDeletionReason.value)
    return

  deleteUserMutation.mutate({ id: userId, mode, reason: deletionReason.value.trim() })
}

function updateUserRole() {
  const userId = selectedUser.value?.id
  if (!userId || !hasRoleChange.value)
    return

  roleAssignmentBlockers.value = null
  changeRoleMutation.mutate({
    id: userId,
    role: Number(newRole.value) as NoCtfDomainIdentityUserRole,
  })
}

function issueBotToken() {
  const userId = selectedUser.value?.id
  if (!userId)
    return

  issueTokenMutation.mutate({
    id: userId,
    expiresInSeconds: Number(tokenLifetime.value),
    userName: selectedUser.value?.userName ?? userId,
  })
}

function invalidateUserTokens() {
  const userId = selectedUser.value?.id
  if (userId)
    invalidateTokensMutation.mutate(userId)
}

async function copyIssuedToken() {
  if (!issuedToken.value)
    return

  try {
    await navigator.clipboard.writeText(issuedToken.value.accessToken)
    tokenCopied.value = true
    toast.success(t('admin.users.tokenCopied'))
  }
  catch {
    toast.error(t('admin.users.actionErrorCopyToken'))
  }
}

function clearIssuedToken() {
  issuedToken.value = null
  tokenCopied.value = false
  issueTokenMutation.reset()
}

function onIssuedTokenDialogChange(open: boolean) {
  if (!open)
    clearIssuedToken()
}

function displayName(user: PlatformUser) {
  return user.userName || user.id || t('admin.users.unknownUser')
}

function formatTimestamp(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value

  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

function roleVariant(role?: NoCtfDomainIdentityUserRole): 'default' | 'secondary' | 'destructive' {
  if (role === PLATFORM_USER_ROLE.administrator)
    return 'destructive'
  if (role === PLATFORM_USER_ROLE.organizer)
    return 'default'
  return 'secondary'
}

function statusVariant(status?: number): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === PLATFORM_USER_ACCOUNT_STATUS.active)
    return 'default'
  if (status === PLATFORM_USER_ACCOUNT_STATUS.banned)
    return 'destructive'
  if (status === PLATFORM_USER_ACCOUNT_STATUS.disabled)
    return 'secondary'
  return 'outline'
}

const columnHelper = createColumnHelper<PlatformUser>()

const columns = [
  columnHelper.accessor(user => user.userName ?? '', {
    id: 'userName',
    header: t('admin.users.username'),
    enableSorting: true,
    cell: (info) => {
      const user = info.row.original
      return h('div', { class: 'flex items-center gap-2' }, [
        h(isBot(user) ? BotIcon : UserIcon, { class: 'size-3.5 text-muted-foreground' }),
        h('span', { class: 'font-medium' }, displayName(user)),
      ])
    },
  }),
  columnHelper.accessor(user => (isBot(user) ? '' : (user.email ?? '')), {
    id: 'email',
    header: t('admin.users.email'),
    enableSorting: true,
    cell: info =>
      isBot(info.row.original)
        ? h('span', { class: 'text-muted-foreground' }, t('admin.users.noEmail'))
        : info.getValue(),
  }),
  columnHelper.accessor('kind', {
    header: t('admin.users.kind'),
    cell: info =>
      h(Badge, { variant: isBot(info.row.original) ? 'default' : 'outline' }, () =>
        t(userKindLabelKey(info.getValue(), info.row.original.role))),
  }),
  columnHelper.accessor('role', {
    header: t('admin.users.role'),
    cell: info =>
      h(Badge, { variant: roleVariant(info.getValue()) }, () =>
        t(userRoleLabelKey(info.getValue()))),
  }),
  columnHelper.accessor('accountStatus', {
    header: t('admin.users.accountStatus'),
    cell: info =>
      h(Badge, { variant: statusVariant(info.getValue()) }, () =>
        t(userAccountStatusLabelKey(info.getValue()))),
  }),
  columnHelper.accessor('tokenVersion', {
    header: t('admin.users.tokenVersion'),
    cell: info =>
      h(
        'code',
        { class: 'font-mono text-xs tabular-nums text-muted-foreground' },
        `v${info.getValue() ?? 0}`,
      ),
  }),
]

const table = useVueTable({
  get data() {
    return users.value ?? []
  },
  columns,
  state: {
    get globalFilter() {
      return globalFilter.value
    },
    get sorting() {
      return sorting.value
    },
  },
  onGlobalFilterChange: (value) => {
    globalFilter.value = value
  },
  onSortingChange: (updater) => {
    sorting.value = typeof updater === 'function' ? updater(sorting.value) : updater
  },
  getCoreRowModel: getCoreRowModel(),
  getFilteredRowModel: getFilteredRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
  getSortedRowModel: getSortedRowModel(),
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
      <div class="space-y-1">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">
            {{ t('admin.users.title') }}
          </h2>
          <Badge
            variant="destructive"
            class="h-4 px-1.5 text-[10px] font-black uppercase tracking-widest"
          >
            {{ t('admin.users.adminOnly') }}
          </Badge>
        </div>
        <p class="max-w-2xl text-sm text-muted-foreground">
          {{ t('admin.users.subtitle') }}
        </p>
      </div>
      <Button class="w-full sm:w-auto" @click="openCreateBotDialog">
        <Plus class="size-4" />
        {{ t('admin.users.createBot') }}
      </Button>
    </div>

    <Card class="grid gap-3 p-3 sm:grid-cols-[minmax(0,24rem)_auto] sm:items-center">
      <div class="relative w-full">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="globalFilter"
          :placeholder="t('admin.users.searchPlaceholder')"
          class="pl-10"
        />
      </div>
      <p class="text-xs tabular-nums text-muted-foreground sm:text-right">
        {{ t('admin.users.userCount', { count: users?.length ?? 0 }) }}
      </p>
    </Card>

    <Card class="min-w-0 p-0">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              class="h-11 px-4 text-left align-middle font-medium text-muted-foreground"
            >
              <template v-if="!header.isPlaceholder">
                <button
                  v-if="header.column.getCanSort()"
                  type="button"
                  class="flex items-center gap-2 outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  @click="header.column.getToggleSortingHandler()?.($event)"
                >
                  <span>{{ header.column.columnDef.header as string }}</span>
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]">▼</span>
                </button>
                <span v-else>{{ header.column.columnDef.header as string }}</span>
              </template>
            </TableHead>
            <TableHead class="w-[80px] px-4 text-right">
              {{ t('common.actions') }}
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate>
          <template v-if="isLoading">
            <TableRow v-for="index in 4" :key="`user-skeleton-${index}`">
              <TableCell v-for="cellIndex in columns.length + 1" :key="cellIndex" class="px-4 py-3">
                <Skeleton :class="cellIndex === 1 ? 'h-4 w-32' : 'h-4 w-20'" />
              </TableCell>
            </TableRow>
          </template>
          <TableRow v-else-if="isError">
            <TableCell :colspan="columns.length + 1" class="h-32 text-center">
              <div class="flex flex-col items-center justify-center gap-3 text-muted-foreground">
                <span>{{ t('admin.users.loadError', t('errors.loadFailed')) }}</span>
                <Button variant="outline" size="sm" @click="refetch()">
                  {{ t('common.refresh') }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-28 text-center">
              <p class="font-medium">
                {{ t('admin.users.empty') }}
              </p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ t('admin.users.emptyDetail') }}
              </p>
            </TableCell>
          </TableRow>
          <TableRow
            v-for="row in table.getRowModel().rows"
            v-else
            :key="row.id"
            class="group transition-colors hover:bg-muted/50"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8 p-0"
                    :aria-label="t('admin.users.actionsFor', { name: displayName(row.original) })"
                  >
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[210px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem
                    v-if="
                      isHuman(row.original)
                        && row.original.accountStatus === PLATFORM_USER_ACCOUNT_STATUS.active
                    "
                    @select="openRoleDialog(row.original)"
                  >
                    <UserCog class="mr-2 size-4" />
                    {{ t('admin.users.changeRole') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    v-if="
                      isBot(row.original)
                        && row.original.accountStatus === PLATFORM_USER_ACCOUNT_STATUS.active
                    "
                    @select="openIssueTokenDialog(row.original)"
                  >
                    <KeyRound class="mr-2 size-4" />
                    {{ t('admin.users.issueToken') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    v-if="row.original.accountStatus === PLATFORM_USER_ACCOUNT_STATUS.active"
                    class="text-destructive focus:text-destructive"
                    @select="openInvalidateTokensDialog(row.original)"
                  >
                    <ShieldOff class="mr-2 size-4" />
                    {{ t('admin.users.invalidateTokens') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    class="text-destructive focus:text-destructive"
                    @select="openDeletionDialog(row.original)"
                  >
                    <Trash2 class="mr-2 size-4" />
                    {{ t('admin.users.deleteAccount') }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <div
      class="flex flex-col gap-3 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between"
    >
      <p>
        {{
          t('common.pageOf', {
            page: table.getState().pagination.pageIndex + 1,
            total: Math.max(1, table.getPageCount()),
          })
        }}
      </p>
      <div class="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="!table.getCanPreviousPage()"
          @click="table.previousPage()"
        >
          {{ t('common.previous') }}
        </Button>
        <Button
          variant="outline"
          size="sm"
          :disabled="!table.getCanNextPage()"
          @click="table.nextPage()"
        >
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

    <Dialog v-model:open="createBotDialog">
      <DialogContent class="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>{{ t('admin.users.createBot') }}</DialogTitle>
          <DialogDescription>{{ t('admin.users.createBotDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label for="bot-purpose">{{ t('admin.users.botPurpose') }}</Label>
            <Select v-model="newBotRole">
              <SelectTrigger id="bot-purpose">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem :value="String(PLATFORM_USER_ROLE.user)">
                  {{ t('admin.users.notificationRelayBot') }}
                </SelectItem>
                <SelectItem :value="String(PLATFORM_USER_ROLE.organizer)">
                  {{ t('admin.users.gitOpsBot') }}
                </SelectItem>
              </SelectContent>
            </Select>
            <p class="text-xs leading-5 text-muted-foreground">
              {{
                Number(newBotRole) === PLATFORM_USER_ROLE.user
                  ? t('admin.users.notificationRelayBotHint')
                  : t('admin.users.gitOpsBotHint')
              }}
            </p>
          </div>
          <div class="space-y-2">
            <Label for="bot-user-name">{{ t('admin.users.botUserName') }}</Label>
            <Input
              id="bot-user-name"
              v-model="newBotUserName"
              autocomplete="off"
              :placeholder="
                Number(newBotRole) === PLATFORM_USER_ROLE.user
                  ? t('admin.users.notificationRelayBotPlaceholder')
                  : t('admin.users.botUserNamePlaceholder')
              "
            />
            <p class="text-xs text-muted-foreground">
              {{ t('admin.users.botUserNameHint') }}
            </p>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="createBotDialog = false">
            {{ t('common.cancel') }}
          </Button>
          <Button
            :disabled="createBotMutation.isPending.value || !canCreateBot"
            @click="createBotMutation.mutate()"
          >
            <Loader2 v-if="createBotMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('common.create') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="roleDialog">
      <DialogContent class="sm:max-w-[420px]">
        <DialogHeader>
          <DialogTitle>
            {{
              t('admin.users.dialogChangeRole', {
                name: selectedUser ? displayName(selectedUser) : '',
              })
            }}
          </DialogTitle>
          <DialogDescription>{{ t('admin.users.roleDialogDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label for="platform-user-role">{{ t('admin.users.newRole') }}</Label>
            <Select v-model="newRole">
              <SelectTrigger id="platform-user-role">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem :value="String(PLATFORM_USER_ROLE.user)">
                  {{ t('admin.users.roleUser') }}
                </SelectItem>
                <SelectItem :value="String(PLATFORM_USER_ROLE.organizer)">
                  {{ t('admin.users.roleOrganizer') }}
                </SelectItem>
                <SelectItem :value="String(PLATFORM_USER_ROLE.administrator)">
                  {{ t('admin.users.roleAdmin') }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div
            v-if="Number(newRole) === PLATFORM_USER_ROLE.administrator"
            class="flex gap-3 border border-destructive/40 bg-destructive/10 p-3"
          >
            <ShieldAlert class="size-5 shrink-0 text-destructive" />
            <p class="text-xs font-medium leading-relaxed text-destructive">
              {{ t('admin.users.adminRoleWarning') }}
            </p>
          </div>

          <Alert v-if="roleAssignmentBlockers" variant="destructive">
            <AlertTitle>{{ t('admin.users.roleDowngradeBlocked') }}</AlertTitle>
            <AlertDescription class="mt-2 space-y-3">
              <p>{{ t('admin.users.roleDowngradeBlockedHint') }}</p>
              <div v-if="roleAssignmentBlockers.competitionIds.length" class="space-y-1">
                <p class="font-medium">
                  {{ t('admin.users.blockingCompetitions') }}
                </p>
                <code
                  v-for="competitionId in roleAssignmentBlockers.competitionIds"
                  :key="competitionId"
                  class="block break-all font-mono text-xs"
                >
                  {{ competitionId }}
                </code>
              </div>
              <div v-if="roleAssignmentBlockers.challengeIds.length" class="space-y-1">
                <p class="font-medium">
                  {{ t('admin.users.blockingChallenges') }}
                </p>
                <code
                  v-for="challengeId in roleAssignmentBlockers.challengeIds"
                  :key="challengeId"
                  class="block break-all font-mono text-xs"
                >
                  {{ challengeId }}
                </code>
              </div>
            </AlertDescription>
          </Alert>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="roleDialog = false">
            {{ t('common.cancel') }}
          </Button>
          <Button
            :disabled="changeRoleMutation.isPending.value || !hasRoleChange"
            @click="updateUserRole"
          >
            <Loader2 v-if="changeRoleMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="issueTokenDialog">
      <DialogContent class="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>
            {{
              t('admin.users.dialogIssueToken', {
                name: selectedUser ? displayName(selectedUser) : '',
              })
            }}
          </DialogTitle>
          <DialogDescription>{{ t('admin.users.issueTokenDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="space-y-2 py-4">
          <Label for="bot-token-lifetime">{{ t('admin.users.tokenLifetime') }}</Label>
          <Select v-model="tokenLifetime">
            <SelectTrigger id="bot-token-lifetime">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="option in BOT_TOKEN_LIFETIMES"
                :key="option.seconds"
                :value="String(option.seconds)"
              >
                {{ t(option.labelKey) }}
              </SelectItem>
            </SelectContent>
          </Select>
          <p class="text-xs text-muted-foreground">
            {{ t('admin.users.issueTokenHint') }}
          </p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="issueTokenDialog = false">
            {{ t('common.cancel') }}
          </Button>
          <Button :disabled="issueTokenMutation.isPending.value" @click="issueBotToken">
            <Loader2 v-if="issueTokenMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('admin.users.issueToken') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="issuedToken !== null" @update:open="onIssuedTokenDialogChange">
      <DialogContent
        class="sm:max-w-[620px]"
        @escape-key-down.prevent
        @pointer-down-outside.prevent
      >
        <template v-if="issuedToken">
          <DialogHeader>
            <DialogTitle>
              {{ t('admin.users.tokenReady', { name: issuedToken.userName }) }}
            </DialogTitle>
            <DialogDescription>{{ t('admin.users.tokenOneTimeWarning') }}</DialogDescription>
          </DialogHeader>
          <div class="space-y-4 py-2">
            <div class="space-y-2">
              <Label for="issued-bot-token">{{ t('admin.users.accessToken') }}</Label>
              <Textarea
                id="issued-bot-token"
                :model-value="issuedToken.accessToken"
                readonly
                rows="8"
                class="min-h-40 resize-none break-all font-mono text-xs"
              />
            </div>
            <div
              class="flex flex-wrap items-center justify-between gap-2 border bg-muted/40 px-3 py-2 text-xs"
            >
              <span class="font-medium">{{ t('admin.users.expiresAt') }}</span>
              <time class="tabular-nums" :datetime="issuedToken.expiresAt">
                {{ formatTimestamp(issuedToken.expiresAt) }}
              </time>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" @click="clearIssuedToken">
              {{ t('admin.users.closeAndClear') }}
            </Button>
            <Button @click="copyIssuedToken">
              <Check v-if="tokenCopied" class="size-4" />
              <Copy v-else class="size-4" />
              {{ tokenCopied ? t('admin.users.tokenCopied') : t('admin.users.copyToken') }}
            </Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="invalidateTokensDialog">
      <DialogContent class="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle class="text-destructive">
            {{
              t('admin.users.dialogInvalidateTokens', {
                name: selectedUser ? displayName(selectedUser) : '',
              })
            }}
          </DialogTitle>
          <DialogDescription>{{ t('admin.users.invalidateTokensDescription') }}</DialogDescription>
        </DialogHeader>
        <div
          class="border border-destructive/40 bg-destructive/10 p-3 text-xs leading-relaxed text-destructive"
        >
          {{ t('admin.users.invalidateTokensWarning') }}
        </div>
        <DialogFooter>
          <Button variant="outline" @click="invalidateTokensDialog = false">
            {{ t('common.cancel') }}
          </Button>
          <Button
            variant="destructive"
            :disabled="invalidateTokensMutation.isPending.value"
            @click="invalidateUserTokens"
          >
            <Loader2 v-if="invalidateTokensMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('admin.users.invalidateTokens') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="deletionDialog">
      <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-[620px]">
        <DialogHeader>
          <DialogTitle class="text-destructive">
            {{
              t('admin.users.deleteDialogTitle', {
                name: selectedUser ? displayName(selectedUser) : '',
              })
            }}
          </DialogTitle>
          <DialogDescription>{{ t('admin.users.deleteDialogDescription') }}</DialogDescription>
        </DialogHeader>

        <div v-if="deletionPreviewLoading" class="space-y-3 py-3">
          <Skeleton class="h-20 w-full" />
          <Skeleton class="h-28 w-full" />
        </div>

        <Alert v-else-if="deletionPreviewError" variant="destructive">
          <AlertTitle>{{ t('admin.users.deletionPreviewError') }}</AlertTitle>
          <AlertDescription class="mt-3">
            <Button variant="outline" size="sm" @click="loadDeletionPreview">
              {{ t('common.retry') }}
            </Button>
          </AlertDescription>
        </Alert>

        <div v-else-if="deletionPreview" class="space-y-4 py-2">
          <Alert
            v-if="
              deletionPreview.selfDeletionForbidden || deletionPreview.lastAdministratorProtected
            "
            variant="destructive"
          >
            <TriangleAlert class="size-4" />
            <AlertTitle>{{ t('admin.users.deletionBlocked') }}</AlertTitle>
            <AlertDescription>
              {{
                deletionPreview.selfDeletionForbidden
                  ? t('admin.users.selfDeletionForbidden')
                  : t('admin.users.lastAdministratorProtected')
              }}
            </AlertDescription>
          </Alert>

          <Alert v-if="deletionConflictCode" variant="destructive">
            <TriangleAlert class="size-4" />
            <AlertTitle>{{ t('admin.users.deleteConflict') }}</AlertTitle>
            <AlertDescription>
              {{ t(`admin.users.deletionConflict.${deletionConflictCode}`) }}
            </AlertDescription>
          </Alert>

          <div class="border bg-muted/30 p-4">
            <div class="flex items-center justify-between gap-4">
              <p class="font-semibold">
                {{ t('admin.users.deletionImpact') }}
              </p>
              <Badge variant="outline">
                {{
                  t('admin.users.referenceCount', {
                    count:
                      deletionPreview.references?.reduce(
                        (sum, item) => sum + (item.count ?? 0),
                        0,
                      ) ?? 0,
                  })
                }}
              </Badge>
            </div>
            <div v-if="deletionPreview.references?.length" class="mt-3 divide-y border-y">
              <div
                v-for="reference in deletionPreview.references"
                :key="reference.code"
                class="flex items-center justify-between gap-4 py-2 text-sm"
              >
                <span>{{ t(deletionReferenceLabelKey(reference.code)) }}</span>
                <code class="font-mono text-xs tabular-nums">{{ reference.count ?? 0 }}</code>
              </div>
            </div>
            <p v-else class="mt-3 text-sm text-muted-foreground">
              {{ t('admin.users.noDeletionReferences') }}
            </p>
          </div>

          <div
            v-if="deletionPreview.canHardDelete"
            class="border border-destructive/40 bg-destructive/10 p-4 text-sm leading-6 text-destructive"
          >
            <p class="font-semibold">
              {{ t('admin.users.physicalDeleteAvailable') }}
            </p>
            <p>{{ t('admin.users.physicalDeleteWarning') }}</p>
          </div>
          <div
            v-else-if="deletionPreview.canAnonymize"
            class="border border-destructive/40 bg-destructive/10 p-4 text-sm leading-6 text-destructive"
          >
            <p class="font-semibold">
              {{ t('admin.users.anonymizeRequired') }}
            </p>
            <p>{{ t('admin.users.anonymizeWarning') }}</p>
          </div>

          <div
            v-if="deletionPreview.canHardDelete || deletionPreview.canAnonymize"
            class="space-y-2"
          >
            <Label for="user-deletion-reason">{{ t('admin.users.deletionReason') }}</Label>
            <Textarea
              id="user-deletion-reason"
              v-model="deletionReason"
              :placeholder="t('admin.users.deletionReasonPlaceholder')"
              maxlength="500"
              rows="3"
            />
            <div class="flex justify-between gap-3 text-xs text-muted-foreground">
              <span>{{ t('admin.users.deletionReasonHint') }}</span>
              <span class="tabular-nums">{{ deletionReason.length }}/500</span>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" @click="deletionDialog = false">
            {{ t('common.cancel') }}
          </Button>
          <Button
            v-if="deletionPreview?.canHardDelete"
            variant="destructive"
            :disabled="deleteUserMutation.isPending.value || !hasValidDeletionReason"
            @click="deleteUser('HardDelete')"
          >
            <Loader2 v-if="deleteUserMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('admin.users.physicalDelete') }}
          </Button>
          <Button
            v-else-if="deletionPreview?.canAnonymize"
            variant="destructive"
            :disabled="deleteUserMutation.isPending.value || !hasValidDeletionReason"
            @click="deleteUser('Anonymize')"
          >
            <Loader2 v-if="deleteUserMutation.isPending.value" class="size-4 animate-spin" />
            {{ t('admin.users.anonymizeAndDeactivate') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
