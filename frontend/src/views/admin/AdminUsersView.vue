<script setup lang="ts">
import { ref, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import {
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
import { Select } from '@/components/ui/select'
import { Badge } from '@/components/ui/badge'
import PageHeader from '@/components/layout/PageHeader.vue'
import ResponsiveTableShell from '@/components/layout/ResponsiveTableShell.vue'
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
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'

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
const actionError = ref('')

const { data: users, isLoading } = useQuery({
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
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.users.actionErrorRole') },
})

const resetPasswordMutation = useMutation({
  mutationFn: async ({ id, password }: { id: string; password: string }) => {
    await adminApi.resetUserPassword(id, password)
  },
  onSuccess: () => {
    passwordDialog.value = false
    newPassword.value = ''
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.users.actionErrorPassword') },
})

function openRoleDialog(user: UserDto) {
  selectedUser.value = user
  newRole.value = user.role
  actionError.value = ''
  roleDialog.value = true
}

function openPasswordDialog(user: UserDto) {
  selectedUser.value = user
  newPassword.value = ''
  actionError.value = ''
  passwordDialog.value = true
}

function roleVariant(role: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (role === 'admin') return 'destructive'
  if (role === 'organizer') return 'default'
  return 'secondary'
}

const columnHelper = createColumnHelper<UserDto>()

const columns = [
  columnHelper.accessor('userName', { header: t('admin.users.username'), enableSorting: true }),
  columnHelper.accessor('email', { header: t('admin.users.email'), enableSorting: true }),
  columnHelper.accessor('role', {
    header: t('admin.users.role'),
    cell: (info) => h(Badge, { variant: roleVariant(info.getValue()) }, () => info.getValue()),
  }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => {
      const user = info.row.original
      return h('div', { class: 'flex gap-2' }, [
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openRoleDialog(user) }, () => t('admin.users.changeRole')),
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openPasswordDialog(user) }, () => t('admin.users.resetPassword')),
      ])
    },
  }),
]

const table = useVueTable({
  get data() { return users.value ?? [] },
  columns,
  state: {
    get globalFilter() { return globalFilter.value },
    get sorting() { return sorting.value },
  },
  onGlobalFilterChange: (v) => { globalFilter.value = v },
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
  <div class="space-y-4 p-4 md:p-6">
    <PageHeader :title="t('admin.users.title')">
      <template #actions>
        <Badge variant="destructive">{{ t('admin.users.adminOnly') }}</Badge>
      </template>
    </PageHeader>

    <div class="flex items-center gap-3">
      <Input
        v-model="globalFilter"
        :placeholder="t('admin.users.searchPlaceholder')"
        class="max-w-xs"
      />
    </div>

    <ResponsiveTableShell dense>
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              :class="header.column.getCanSort() ? 'cursor-pointer select-none' : ''"
              @click="header.column.getToggleSortingHandler()?.($event)"
            >
              <template v-if="!header.isPlaceholder">
                {{ header.column.columnDef.header as string }}
                <span v-if="header.column.getIsSorted() === 'asc'"> ↑</span>
                <span v-else-if="header.column.getIsSorted() === 'desc'"> ↓</span>
              </template>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-if="isLoading">
            <TableRow>
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">
                {{ t('admin.users.loading') }}
              </TableCell>
            </TableRow>
          </template>
          <template v-else-if="table.getRowModel().rows.length === 0">
            <TableRow>
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">
                {{ t('admin.users.empty') }}
              </TableCell>
            </TableRow>
          </template>
          <template v-else>
            <TableRow v-for="row in table.getRowModel().rows" :key="row.id">
              <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
                <component :is="() => cell.renderValue()" v-if="cell.column.id === 'role' || cell.column.id === 'actions'" />
                <template v-else>{{ cell.getValue() }}</template>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </ResponsiveTableShell>

    <!-- Pagination -->
    <div class="flex items-center justify-between">
      <span class="text-sm text-muted-foreground">
        {{ t('common.pageOf', { page: table.getState().pagination.pageIndex + 1, total: table.getPageCount() }) }}
      </span>
      <div class="flex gap-2">
        <Button size="sm" variant="outline" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">
          {{ t('common.previous') }}
        </Button>
        <Button size="sm" variant="outline" :disabled="!table.getCanNextPage()" @click="table.nextPage()">
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

    <!-- Change Role Dialog -->
    <Dialog v-model:open="roleDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.users.dialogChangeRole', { name: selectedUser?.userName }) }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <Label>{{ t('admin.users.newRole') }}</Label>
          <Select v-model="newRole">
            <option value="user">{{ t('admin.users.roleUser') }}</option>
            <option value="organizer">{{ t('admin.users.roleOrganizer') }}</option>
            <option value="admin">{{ t('admin.users.roleAdmin') }}</option>
          </Select>
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="roleDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            :disabled="changeRoleMutation.isPending.value"
            @click="changeRoleMutation.mutate({ id: selectedUser!.id, role: newRole })"
          >
            {{ t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Reset Password Dialog -->
    <Dialog v-model:open="passwordDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.users.dialogResetPassword', { name: selectedUser?.userName }) }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <Label>{{ t('admin.users.newPassword') }}</Label>
          <Input v-model="newPassword" type="password" :placeholder="t('admin.users.newPassword')" />
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="passwordDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            :disabled="resetPasswordMutation.isPending.value || !newPassword"
            @click="resetPasswordMutation.mutate({ id: selectedUser!.id, password: newPassword })"
          >
            {{ t('common.reset') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
