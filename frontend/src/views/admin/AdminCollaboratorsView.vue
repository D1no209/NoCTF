<script setup lang="ts">
import { computed, ref, watch, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import {
  useVueTable,
  getCoreRowModel,
  getPaginationRowModel,
  createColumnHelper,
} from '@tanstack/vue-table'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useRoute } from 'vue-router'
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
const route = useRoute()
const qc = useQueryClient()

interface CollaboratorDto {
  userId: string
  userName: string
  role: string
}

interface CompetitionOption {
  id: string
  title: string
}

const selectedCompetitionId = ref<string>((route.query.competitionId as string) ?? '')
const selectedCompetitionTitle = ref<string>((route.query.competitionTitle as string) ?? '')

const addDialog = ref(false)
const removeDialog = ref(false)
const selectedCollab = ref<CollaboratorDto | null>(null)
const newUserId = ref('')
const newUserSearch = ref('')
const newRole = ref('Observer')
const actionError = ref('')

// Load competitions for dropdown
const { data: competitions } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: () => adminApi.competitions<CompetitionOption[]>(),
})

// Load collaborators for selected competition
const { data: collaborators, isLoading } = useQuery({
  queryKey: computed(() => queryKeys.adminCollaborators(selectedCompetitionId.value)),
  queryFn: async () => {
    if (!selectedCompetitionId.value) return []
    return adminApi.collaborators<CollaboratorDto[]>(selectedCompetitionId.value)
  },
  enabled: () => !!selectedCompetitionId.value,
})

// User search for add dialog
const { data: userSearchResults } = useQuery({
  queryKey: computed(() => ['admin-user-search', newUserSearch.value]),
  queryFn: async () => {
    if (!newUserSearch.value || newUserSearch.value.length < 2) return []
    const all = await adminApi.users<{ id: string; userName: string }[]>()
    return all.filter(u => u.userName.toLowerCase().includes(newUserSearch.value.toLowerCase()))
  },
  enabled: () => newUserSearch.value.length >= 2,
})

const addMutation = useMutation({
  mutationFn: async () => {
    await adminApi.addCollaborator(selectedCompetitionId.value, { userId: newUserId.value, role: newRole.value })
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    addDialog.value = false
    newUserId.value = ''
    newUserSearch.value = ''
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.collaborators.addError') },
})

const removeMutation = useMutation({
  mutationFn: async (userId: string) => {
    await adminApi.removeCollaborator(selectedCompetitionId.value, userId)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    removeDialog.value = false
  },
})

function openRemove(collab: CollaboratorDto) {
  selectedCollab.value = collab
  removeDialog.value = true
}

function selectUser(user: { id: string; userName: string }) {
  newUserId.value = user.id
  newUserSearch.value = user.userName
}

watch(() => route.query.competitionId, (id) => {
  if (id) {
    selectedCompetitionId.value = id as string
    selectedCompetitionTitle.value = route.query.competitionTitle as string ?? ''
  }
})

const columnHelper = createColumnHelper<CollaboratorDto>()

const columns = [
  columnHelper.accessor('userName', { header: t('admin.collaborators.username') }),
  columnHelper.accessor('role', {
    header: t('admin.collaborators.role'),
    cell: (info) => h(Badge, { variant: info.getValue() === 'manager' ? 'default' : 'secondary' }, () => info.getValue()),
  }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => h(Button, {
      size: 'sm',
      variant: 'destructive',
      onClick: () => openRemove(info.row.original),
    }, () => t('admin.collaborators.remove')),
  }),
]

const table = useVueTable({
  get data() { return collaborators.value ?? [] },
  columns,
  getCoreRowModel: getCoreRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
})
</script>

<template>
  <div class="space-y-4 p-4 md:p-6">
    <PageHeader :title="t('admin.collaborators.title')" />

    <!-- Competition selector -->
    <div class="flex items-center gap-3">
      <Label class="shrink-0">{{ t('admin.collaborators.competitionLabel') }}</Label>
      <Select
        v-model="selectedCompetitionId"
        class="max-w-xs"
      >
        <option value="">{{ t('admin.collaborators.selectCompetition') }}</option>
        <option v-for="c in competitions" :key="c.id" :value="c.id">{{ c.title }}</option>
      </Select>
      <Button v-if="selectedCompetitionId" @click="addDialog = true">{{ t('admin.collaborators.addCollaborator') }}</Button>
    </div>

    <template v-if="selectedCompetitionId">
      <ResponsiveTableShell dense>
        <Table>
          <TableHeader>
            <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
              <TableHead v-for="header in headerGroup.headers" :key="header.id">
                <template v-if="!header.isPlaceholder">{{ header.column.columnDef.header as string }}</template>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="isLoading">
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.collaborators.loading') }}</TableCell>
            </TableRow>
            <TableRow v-else-if="table.getRowModel().rows.length === 0">
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.collaborators.empty') }}</TableCell>
            </TableRow>
            <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
              <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
                <component :is="() => cell.renderValue()" v-if="['role', 'actions'].includes(cell.column.id)" />
                <template v-else>{{ cell.getValue() }}</template>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </ResponsiveTableShell>

      <div class="flex items-center justify-between">
        <span class="text-sm text-muted-foreground">
          {{ t('common.pageOf', { page: table.getState().pagination.pageIndex + 1, total: table.getPageCount() }) }}
        </span>
        <div class="flex gap-2">
          <Button size="sm" variant="outline" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">{{ t('common.previous') }}</Button>
          <Button size="sm" variant="outline" :disabled="!table.getCanNextPage()" @click="table.nextPage()">{{ t('common.next') }}</Button>
        </div>
      </div>
    </template>

    <div v-else class="text-muted-foreground text-sm">{{ t('admin.collaborators.selectPrompt') }}</div>

    <!-- Add Collaborator Dialog -->
    <Dialog v-model:open="addDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.collaborators.addDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <div>
            <Label>{{ t('admin.collaborators.searchUser') }}</Label>
            <Input v-model="newUserSearch" :placeholder="t('admin.collaborators.typeUsername')" class="mt-1" />
            <ul v-if="userSearchResults && userSearchResults.length > 0" class="border rounded-md mt-1 max-h-32 overflow-y-auto">
              <li
                v-for="u in userSearchResults"
                :key="u.id"
                class="px-3 py-1.5 text-sm cursor-pointer hover:bg-accent"
                :class="newUserId === u.id ? 'bg-accent font-medium' : ''"
                @click="selectUser(u)"
              >
                {{ u.userName }}
              </li>
            </ul>
          </div>
          <div>
            <Label>{{ t('admin.collaborators.role') }}</Label>
            <Select v-model="newRole" class="mt-1">
              <option value="Manager">{{ t('admin.collaborators.roleManager') }}</option>
              <option value="Observer">{{ t('admin.collaborators.roleObserver') }}</option>
            </Select>
          </div>
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="addDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            :disabled="addMutation.isPending.value || !newUserId"
            @click="addMutation.mutate()"
          >
            {{ t('common.add') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Remove Confirmation -->
    <Dialog v-model:open="removeDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.collaborators.removeDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm py-2" v-html="t('admin.collaborators.removeConfirm', { name: selectedCollab?.userName })"></p>
        <DialogFooter>
          <Button variant="outline" @click="removeDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="removeMutation.isPending.value"
            @click="removeMutation.mutate(selectedCollab!.userId)"
          >
            {{ t('admin.collaborators.remove') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
