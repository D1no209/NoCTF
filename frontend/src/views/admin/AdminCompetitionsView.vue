<script setup lang="ts">
import { computed, ref, h } from 'vue'
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
  SelectValue 
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
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { useRouter } from 'vue-router'
import { 
  MoreHorizontal, 
  Plus, 
  Search, 
  Settings, 
  Users2, 
  Trash2, 
  ExternalLink,
  Loader2
} from 'lucide-vue-next'
import { toast } from 'vue-sonner'

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

const form = ref({
  title: '',
  description: '',
  gameModeType: 'Ctf',
  startTime: '',
  endTime: '',
  status: 'Draft',
})

function toDateTimeLocal(date: Date) {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function defaultSchedule() {
  const start = new Date()
  start.setMinutes(start.getMinutes() + 5)
  const end = new Date(start)
  end.setHours(end.getHours() + 2)
  return {
    startTime: toDateTimeLocal(start),
    endTime: toDateTimeLocal(end),
  }
}

const isScheduleValid = computed(() => {
  if (!form.value.startTime || !form.value.endTime) return false
  return new Date(form.value.endTime).getTime() > new Date(form.value.startTime).getTime()
})

const canSave = computed(() => Boolean(form.value.title.trim()) && isScheduleValid.value)

function competitionPayload() {
  return {
    ...form.value,
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    startTime: new Date(form.value.startTime).toISOString(),
    endTime: new Date(form.value.endTime).toISOString(),
  }
}

const { data: competitions, isLoading } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: () => adminApi.competitions<CompetitionAdminDto[]>(),
})

const saveMutation = useMutation({
  mutationFn: async () => {
    if (!canSave.value) {
      throw new Error('invalid_competition_form')
    }
    const body = competitionPayload()
    if (isCreating.value) {
      await adminApi.createCompetition(body)
    } else {
      await adminApi.updateCompetition(selectedComp.value!.id, body)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    editDialog.value = false
    toast.success(isCreating.value ? t('admin.competitions.createSuccess') : t('admin.competitions.updateSuccess'))
  },
  onError: () => {
    toast.error(t('admin.competitions.saveError'))
  },
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => {
    await adminApi.deleteCompetition(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    deleteDialog.value = false
    toast.success(t('admin.competitions.deleteSuccess'))
  },
  onError: () => {
    toast.error(t('admin.competitions.deleteError'))
  }
})

function openCreate() {
  isCreating.value = true
  selectedComp.value = null
  form.value = { title: '', description: '', gameModeType: 'Ctf', status: 'Draft', ...defaultSchedule() }
  editDialog.value = true
}

function openDelete(comp: CompetitionAdminDto) {
  selectedComp.value = comp
  deleteDialog.value = true
}

function goCollaborators(comp: CompetitionAdminDto) {
  router.push({ name: 'admin-collaborators', query: { competitionId: comp.id, competitionTitle: comp.title } })
}

function goManage(comp: CompetitionAdminDto) {
  router.push({ name: 'admin-competition-detail', params: { id: comp.id } })
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'active') return 'default'
  if (s === 'published') return 'secondary'
  if (s === 'finished' || s === 'ended') return 'outline'
  if (s === 'draft') return 'outline'
  return 'secondary'
}

const columnHelper = createColumnHelper<CompetitionAdminDto>()

const columns = [
  columnHelper.accessor('title', { 
    header: t('admin.competitions.titleColumn'), 
    enableSorting: true,
    cell: (info) => info.getValue() 
  }),
  columnHelper.accessor('gameModeType', { 
    header: t('admin.competitions.mode'), 
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'outline', class: 'font-mono' }, () => info.getValue())
  }),
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
  <div class="space-y-6">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.competitions.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.competitions.subtitle', 'Manage and monitor all competitions.') }}</p>
      </div>
      <Button @click="openCreate" class="shrink-0">
        <Plus class="mr-2 size-4" />
        {{ t('admin.competitions.create') }}
      </Button>
    </div>

    <div class="flex items-center gap-2">
      <div class="relative w-full max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="globalFilter" :placeholder="t('admin.competitions.searchPlaceholder')" class="pl-10" />
      </div>
    </div>

    <div class="rounded-xl border bg-card shadow-sm overflow-hidden">
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
                  <FlexRender :render="header.column.columnDef.header" :props="header.getContext()" />
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]">▼</span>
                </div>
              </template>
            </TableHead>
            <TableHead class="w-[80px] text-right px-4">{{ t('common.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center">
              <div class="flex items-center justify-center gap-2 text-muted-foreground">
                <Loader2 class="size-4 animate-spin" />
                <span>{{ t('admin.competitions.loading') }}</span>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.competitions.empty') }}
            </TableCell>
          </TableRow>
          <TableRow 
            v-else 
            v-for="row in table.getRowModel().rows" 
            :key="row.id"
            class="group transition-colors hover:bg-muted/50"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" class="size-8 h-8 w-8 p-0">
                    <span class="sr-only">Open menu</span>
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[160px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem @click="goManage(row.original)">
                    <Settings class="mr-2 size-4" />
                    Manage
                  </DropdownMenuItem>
                  <DropdownMenuItem @click="goCollaborators(row.original)">
                    <Users2 class="mr-2 size-4" />
                    {{ t('admin.competitions.collaborators') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @click="router.push(`/competitions/${row.original.id}`)">
                    <ExternalLink class="mr-2 size-4" />
                    {{ t('admin.competitions.viewPublic') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @click="openDelete(row.original)" class="text-destructive focus:text-destructive">
                    <Trash2 class="mr-2 size-4" />
                    {{ t('common.delete') }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div class="flex items-center justify-between">
      <p class="text-xs text-muted-foreground">
        {{ t('common.pageOf', { page: table.getState().pagination.pageIndex + 1, total: table.getPageCount() }) }}
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

    <!-- Create/Edit Dialog -->
    <Dialog v-model:open="editDialog">
      <DialogContent class="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? t('admin.competitions.createDialogTitle') : t('admin.competitions.editDialogTitle') }}</DialogTitle>
          <DialogDescription>
            {{ isCreating ? 'Set up a new competition.' : 'Update existing competition details.' }}
          </DialogDescription>
        </DialogHeader>
        <div class="grid gap-4 py-4">
          <div class="grid gap-2">
            <Label for="title">{{ t('admin.competitions.titleColumn') }}</Label>
            <Input id="title" v-model="form.title" :placeholder="t('admin.competitions.titleColumn')" />
          </div>
          <div class="grid gap-2">
            <Label for="description">{{ t('admin.competitions.description') }}</Label>
            <Input id="description" v-model="form.description" :placeholder="t('admin.competitions.description')" />
          </div>
          <div class="grid grid-cols-2 gap-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.gameMode') }}</Label>
              <Select v-model="form.gameModeType">
                <SelectTrigger>
                  <SelectValue placeholder="Select mode" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Ctf">{{ t('admin.competitions.modeCtf') }}</SelectItem>
                  <SelectItem value="Awd">{{ t('admin.competitions.modeAwd') }}</SelectItem>
                  <SelectItem value="Awdp">{{ t('admin.competitions.modeAwdp') }}</SelectItem>
                  <SelectItem value="Koh">{{ t('admin.competitions.modeKoh') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.status') }}</Label>
              <Select v-model="form.status">
                <SelectTrigger>
                  <SelectValue placeholder="Select status" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Draft">{{ t('competitions.status.draft') }}</SelectItem>
                  <SelectItem value="Published">{{ t('competitions.status.published') }}</SelectItem>
                  <SelectItem value="Running">{{ t('competitions.status.running') }}</SelectItem>
                  <SelectItem value="Paused">{{ t('competitions.status.paused') }}</SelectItem>
                  <SelectItem value="Finished">{{ t('competitions.status.finished') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.startTime') }}</Label>
              <Input v-model="form.startTime" type="datetime-local" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.endTime') }}</Label>
              <Input v-model="form.endTime" type="datetime-local" />
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="editDialog = false" :disabled="saveMutation.isPending.value">{{ t('common.cancel') }}</Button>
          <Button :disabled="saveMutation.isPending.value || !canSave" @click="saveMutation.mutate()">
            <Loader2 v-if="saveMutation.isPending.value" class="mr-2 size-4 animate-spin" />
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
          <DialogDescription>
            This action cannot be undone. This will permanently delete the competition and all associated data.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <p class="text-sm font-medium">Are you sure you want to delete <span class="font-bold text-foreground">"{{ selectedComp?.title }}"</span>?</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="deleteMutation.isPending.value"
            @click="deleteMutation.mutate(selectedComp!.id)"
          >
            <Loader2 v-if="deleteMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('common.delete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
