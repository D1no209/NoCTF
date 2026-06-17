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
import { useRouter } from 'vue-router'

const { t } = useI18n()
const qc = useQueryClient()
const router = useRouter()

interface CompetitionAdminDto {
  id: string
  title: string
  description?: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
  ownerId: string
}

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const selectedComp = ref<CompetitionAdminDto | null>(null)
const isCreating = ref(false)
const actionError = ref('')

const form = ref({
  title: '',
  description: '',
  gameModeType: 'Ctf',
  startTime: '',
  endTime: '',
  status: 'Draft',
})

const { data: competitions, isLoading } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: () => adminApi.competitions<CompetitionAdminDto[]>(),
})

const saveMutation = useMutation({
  mutationFn: async () => {
    if (isCreating.value) {
      await adminApi.createCompetition(form.value)
    } else {
      await adminApi.updateCompetition(selectedComp.value!.id, form.value)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    editDialog.value = false
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.competitions.saveError') },
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => {
    await adminApi.deleteCompetition(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    deleteDialog.value = false
  },
})

function openCreate() {
  isCreating.value = true
  selectedComp.value = null
  form.value = { title: '', description: '', gameModeType: 'Ctf', startTime: '', endTime: '', status: 'Draft' }
  actionError.value = ''
  editDialog.value = true
}

function openEdit(comp: CompetitionAdminDto) {
  isCreating.value = false
  selectedComp.value = comp
  form.value = {
    title: comp.title,
    description: comp.description ?? '',
    gameModeType: comp.gameModeType,
    startTime: comp.startTime ? comp.startTime.slice(0, 16) : '',
    endTime: comp.endTime ? comp.endTime.slice(0, 16) : '',
    status: comp.status,
  }
  actionError.value = ''
  editDialog.value = true
}

function openDelete(comp: CompetitionAdminDto) {
  selectedComp.value = comp
  deleteDialog.value = true
}

function goCollaborators(comp: CompetitionAdminDto) {
  router.push({ name: 'admin-collaborators', query: { competitionId: comp.id, competitionTitle: comp.title } })
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 'running') return 'default'
  if (status === 'published') return 'secondary'
  if (status === 'finished') return 'outline'
  if (status === 'draft') return 'outline'
  return 'secondary'
}

const columnHelper = createColumnHelper<CompetitionAdminDto>()

const columns = [
  columnHelper.accessor('title', { header: t('admin.competitions.titleColumn'), enableSorting: true }),
  columnHelper.accessor('gameModeType', { header: t('admin.competitions.mode'), enableSorting: true }),
  columnHelper.accessor('status', {
    header: t('admin.competitions.status'),
    cell: (info) => h(Badge, { variant: statusVariant(info.getValue()) }, () => info.getValue()),
  }),
  columnHelper.accessor('startTime', {
    header: t('admin.competitions.start'),
    cell: (info) => new Date(info.getValue()).toLocaleDateString(),
  }),
  columnHelper.accessor('endTime', {
    header: t('admin.competitions.end'),
    cell: (info) => new Date(info.getValue()).toLocaleDateString(),
  }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => {
      const comp = info.row.original
      return h('div', { class: 'flex gap-1 flex-wrap' }, [
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openEdit(comp) }, () => t('common.edit')),
        h(Button, { size: 'sm', variant: 'outline', onClick: () => goCollaborators(comp) }, () => t('admin.competitions.collaborators')),
        h(Button, { size: 'sm', variant: 'destructive', onClick: () => openDelete(comp) }, () => t('common.delete')),
      ])
    },
  }),
]

const table = useVueTable({
  get data() { return competitions.value ?? [] },
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
    <PageHeader :title="t('admin.competitions.title')">
      <template #actions>
        <Button @click="openCreate">{{ t('admin.competitions.create') }}</Button>
      </template>
    </PageHeader>

    <Input v-model="globalFilter" :placeholder="t('admin.competitions.searchPlaceholder')" class="max-w-xs" />

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
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.competitions.loading') }}</TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.competitions.empty') }}</TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
              <component :is="() => cell.renderValue()" v-if="['status', 'actions', 'startTime', 'endTime'].includes(cell.column.id)" />
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

    <!-- Create/Edit Dialog -->
    <Dialog v-model:open="editDialog">
      <DialogContent class="max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? t('admin.competitions.createDialogTitle') : t('admin.competitions.editDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <div>
            <Label>{{ t('admin.competitions.titleColumn') }}</Label>
            <Input v-model="form.title" :placeholder="t('admin.competitions.titleColumn')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.competitions.description') }}</Label>
            <Input v-model="form.description" :placeholder="t('admin.competitions.description')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.competitions.gameMode') }}</Label>
            <Select v-model="form.gameModeType" class="mt-1">
              <option value="Ctf">{{ t('admin.competitions.modeCtf') }}</option>
              <option value="Awd">{{ t('admin.competitions.modeAwd') }}</option>
              <option value="Awdp">{{ t('admin.competitions.modeAwdp') }}</option>
              <option value="Koh">{{ t('admin.competitions.modeKoh') }}</option>
            </Select>
          </div>
          <div>
            <Label>{{ t('admin.competitions.status') }}</Label>
            <Select v-model="form.status" class="mt-1">
              <option value="Draft">{{ t('competitions.status.draft') }}</option>
              <option value="Published">{{ t('competitions.status.published') }}</option>
              <option value="Running">{{ t('competitions.status.running') }}</option>
              <option value="Paused">{{ t('competitions.status.paused') }}</option>
              <option value="Finished">{{ t('competitions.status.finished') }}</option>
            </Select>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <Label>{{ t('admin.competitions.startTime') }}</Label>
              <Input v-model="form.startTime" type="datetime-local" class="mt-1" />
            </div>
            <div>
              <Label>{{ t('admin.competitions.endTime') }}</Label>
              <Input v-model="form.endTime" type="datetime-local" class="mt-1" />
            </div>
          </div>
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="editDialog = false">{{ t('common.cancel') }}</Button>
          <Button :disabled="saveMutation.isPending.value || !form.title" @click="saveMutation.mutate()">
            {{ isCreating ? t('common.create') : t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Delete Confirmation -->
    <Dialog v-model:open="deleteDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.competitions.deleteDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm py-2" v-html="t('admin.competitions.deleteConfirm', { title: selectedComp?.title })"></p>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="deleteMutation.isPending.value"
            @click="deleteMutation.mutate(selectedComp!.id)"
          >
            {{ t('common.delete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
