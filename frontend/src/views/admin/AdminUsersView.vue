<script setup lang="ts">
import { ref, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import {
  FlexRender,
  useVueTable,
  getCoreRowModel,
  getPaginationRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  createColumnHelper,
  type SortingState,
} from '@tanstack/vue-table'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import {
  Search,
  MoreHorizontal,
  UserCog,
  KeyRound,
  ShieldAlert,
  User as UserIcon,
  Loader2,
  Lock,
} from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { userRoleVariant } from '@/lib/statusTones'

const { t } = useI18n()
const qc = useQueryClient()

interface UserDto {
  id: string
  userName: string
  email: string
  role: string
}

const globalFilter = ref('')
const sorting = ref<SortingState>([])

// Dialogs
const roleDialog = ref(false)
const passwordDialog = ref(false)
const selectedUser = ref<UserDto | null>(null)
const newRole = ref('user')
const newPassword = ref('')

const {
  data: users,
  isLoading,
  isError,
  refetch,
} = useQuery({
  queryKey: queryKeys.adminUsers,
  queryFn: () => adminApi.users<UserDto[]>(),
})

const changeRoleMutation = useMutation({
  mutationFn: async ({ id, role }: { id: string; role: string }) => {
    await adminApi.updateUserRole(id, role)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminUsers })
    roleDialog.value = false
    toast.success(t('admin.users.roleUpdateSuccess', 'User role updated.'))
  },
  onError: () => {
    toast.error(t('admin.users.actionErrorRole'))
  },
})

const resetPasswordMutation = useMutation({
  mutationFn: async ({ id, password }: { id: string; password: string }) => {
    await adminApi.resetUserPassword(id, password)
  },
  onSuccess: () => {
    passwordDialog.value = false
    newPassword.value = ''
    toast.success(t('admin.users.passwordResetSuccess', 'Password reset successfully.'))
  },
  onError: () => {
    toast.error(t('admin.users.actionErrorPassword'))
  },
})

function openRoleDialog(user: UserDto) {
  selectedUser.value = user
  newRole.value = user.role.toLowerCase()
  roleDialog.value = true
}

function openPasswordDialog(user: UserDto) {
  selectedUser.value = user
  newPassword.value = ''
  passwordDialog.value = true
}

const columnHelper = createColumnHelper<UserDto>()

const columns = [
  columnHelper.accessor('userName', {
    header: t('admin.users.username'),
    enableSorting: true,
    cell: (info) =>
      h('div', { class: 'flex items-center gap-2' }, [
        h(UserIcon, { class: 'size-3.5 text-muted-foreground' }),
        h('span', { class: 'font-medium' }, info.getValue()),
      ]),
  }),
  columnHelper.accessor('email', {
    header: t('admin.users.email'),
    enableSorting: true,
  }),
  columnHelper.accessor('role', {
    header: t('admin.users.role'),
    cell: (info) => h(Badge, { variant: userRoleVariant(info.getValue()) }, () => info.getValue()),
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
  onGlobalFilterChange: (v) => {
    globalFilter.value = v
  },
  onSortingChange: (updater) => {
    sorting.value = typeof updater === 'function' ? updater(sorting.value) : updater
  },
  getCoreRowModel: getCoreRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
  getFilteredRowModel: getFilteredRowModel(),
  getSortedRowModel: getSortedRowModel(),
})
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <div class="flex items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.users.title') }}</h2>
          <Badge
            variant="destructive"
            class="text-[10px] uppercase font-black tracking-widest px-1.5 h-4"
          >
            {{ t('admin.users.adminOnly') }}
          </Badge>
        </div>
        <p class="text-sm text-muted-foreground">{{ t('admin.users.subtitle') }}</p>
      </div>
    </div>

    <div class="noctf-filter-bar md:grid-cols-[minmax(0,24rem)]">
      <div class="relative w-full max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="globalFilter"
          :placeholder="t('admin.users.searchPlaceholder')"
          class="pl-10"
        />
      </div>
    </div>

    <div class="noctf-table-shell">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              class="h-11 px-4 text-left align-middle font-medium text-muted-foreground"
              :class="header.column.getCanSort() ? 'cursor-pointer select-none' : ''"
              @click="header.column.getToggleSortingHandler()?.($event)"
            >
              <template v-if="!header.isPlaceholder">
                <div class="flex items-center gap-2">
                  <span>{{ header.column.columnDef.header as string }}</span>
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]"
                    >▼</span
                  >
                </div>
              </template>
            </TableHead>
            <TableHead class="w-[80px] text-right px-4">{{ t('common.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate="{ duration: 140, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }">
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center">
              <div class="flex items-center justify-center gap-2 text-muted-foreground">
                <Loader2 class="size-4 animate-spin" />
                <span>{{ t('admin.users.loading') }}</span>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="isError">
            <TableCell :colspan="columns.length + 1" class="h-32 text-center">
              <div class="flex flex-col items-center justify-center gap-3 text-muted-foreground">
                <span>{{ t('admin.users.loadError', t('errors.loadFailed')) }}</span>
                <Button variant="outline" size="sm" @click="refetch()">{{
                  t('common.refresh')
                }}</Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.users.empty') }}
            </TableCell>
          </TableRow>
          <TableRow
            v-else
            v-for="row in table.getRowModel().rows"
            :key="row.id"
            class="group transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-muted/50"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" class="size-8 p-0">
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[180px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem @click="openRoleDialog(row.original)">
                    <UserCog class="mr-2 size-4" />
                    {{ t('admin.users.changeRole') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem @click="openPasswordDialog(row.original)">
                    <KeyRound class="mr-2 size-4" />
                    {{ t('admin.users.resetPassword') }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div class="noctf-table-footer">
      <p class="text-xs text-muted-foreground">
        {{
          t('common.pageOf', {
            page: table.getState().pagination.pageIndex + 1,
            total: table.getPageCount(),
          })
        }}
      </p>
      <div class="flex items-center space-x-2">
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

    <!-- Change Role Dialog -->
    <Dialog v-model:open="roleDialog">
      <DialogContent class="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>{{
            t('admin.users.dialogChangeRole', { name: selectedUser?.userName })
          }}</DialogTitle>
          <DialogDescription>{{ t('admin.users.roleDialogDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4">
          <div class="space-y-2">
            <Label>{{ t('admin.users.newRole') }}</Label>
            <Select v-model="newRole">
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="user">{{ t('admin.users.roleUser') }}</SelectItem>
                <SelectItem value="organizer">{{ t('admin.users.roleOrganizer') }}</SelectItem>
                <SelectItem value="admin">{{ t('admin.users.roleAdmin') }}</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div
            v-if="newRole === 'admin'"
            class="p-3 rounded-lg bg-danger-muted/70 border border-danger/25 flex gap-3"
          >
            <ShieldAlert class="size-5 text-danger shrink-0" />
            <p class="text-xs text-danger font-medium leading-tight">
              {{ t('admin.users.adminRoleWarning') }}
            </p>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="roleDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            :disabled="changeRoleMutation.isPending.value"
            @click="changeRoleMutation.mutate({ id: selectedUser!.id, role: newRole })"
          >
            <Loader2 v-if="changeRoleMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Reset Password Dialog -->
    <Dialog v-model:open="passwordDialog">
      <DialogContent class="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>{{
            t('admin.users.dialogResetPassword', { name: selectedUser?.userName })
          }}</DialogTitle>
          <DialogDescription>{{ t('admin.users.passwordDialogDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4">
          <div class="space-y-2">
            <Label>{{ t('admin.users.newPassword') }}</Label>
            <div class="relative">
              <Lock class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                v-model="newPassword"
                type="password"
                class="pl-10"
                :placeholder="t('validation.passwordMin')"
              />
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="passwordDialog = false">{{
            t('common.cancel')
          }}</Button>
          <Button
            :disabled="
              resetPasswordMutation.isPending.value || !newPassword || newPassword.length < 8
            "
            @click="resetPasswordMutation.mutate({ id: selectedUser!.id, password: newPassword })"
          >
            <Loader2
              v-if="resetPasswordMutation.isPending.value"
              class="mr-2 size-4 animate-spin"
            />
            {{ t('common.reset') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
