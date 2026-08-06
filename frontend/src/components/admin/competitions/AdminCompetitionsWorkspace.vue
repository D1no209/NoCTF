<script setup lang="ts">
import type { SortingState } from '@tanstack/vue-table'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionRequest,
  NoCtfDomainCompetitionsGameMode,
} from '@/api/generated/types.gen'
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
  Archive,
  ExternalLink,
  Loader2,
  MoreHorizontal,
  Settings,
  ShieldCheck,
  Trash2,
} from 'lucide-vue-next'
import { computed, h, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionDeleteDialog from '@/components/admin/competitions/AdminCompetitionDeleteDialog.vue'
import AdminCompetitionEditorDialog from '@/components/admin/competitions/AdminCompetitionEditorDialog.vue'
import AdminCompetitionsToolbar from '@/components/admin/competitions/AdminCompetitionsToolbar.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

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
  teamRegistrationAutoApprove: boolean
  maxTeamMembers: number
  maxConcurrentRuntimeInstancesPerTeam: number
  ownerId: string
}

const competitionModes = ['Ctf', 'Awd', 'Awdp', 'Koh'] as const
const competitionStatuses = ['Draft', 'Visible', 'Published', 'Running', 'Paused', 'Finished'] as const

function toCompetitionAdminDto(value: Awaited<ReturnType<typeof competitionAdminApi.list>>[number]): CompetitionAdminDto {
  if (
    !value.id
    || !value.title
    || value.mode === undefined
    || value.status === undefined
    || !value.startTime
    || !value.endTime
    || value.teamRegistrationAutoApprove === undefined
    || value.maxTeamMembers === undefined
    || value.maxConcurrentRuntimeInstancesPerTeam === undefined
    || !value.ownerId
  ) {
    throw new TypeError('Admin competition response is incomplete.')
  }

  return {
    id: value.id,
    title: value.title,
    description: value.description ?? undefined,
    gameModeType: competitionModes[value.mode],
    status: competitionStatuses[value.status],
    startTime: value.startTime,
    endTime: value.endTime,
    teamRegistrationAutoApprove: value.teamRegistrationAutoApprove,
    maxTeamMembers: value.maxTeamMembers,
    maxConcurrentRuntimeInstancesPerTeam: value.maxConcurrentRuntimeInstancesPerTeam,
    ownerId: value.ownerId,
  }
}

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const archiveDialog = ref(false)
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
  if (!form.value.startTime || !form.value.endTime)
    return false
  return new Date(form.value.endTime).getTime() > new Date(form.value.startTime).getTime()
})

const canSave = computed(() => Boolean(form.value.title.trim()) && isScheduleValid.value)

function competitionMode(value: string): NoCtfDomainCompetitionsGameMode {
  switch (value) {
    case 'Ctf': return 0
    case 'Awd': return 1
    case 'Awdp': return 2
    case 'Koh': return 3
    default: throw new Error('invalid_competition_mode')
  }
}

function competitionPayload(): NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionRequest {
  return {
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    startTime: new Date(form.value.startTime).toISOString(),
    endTime: new Date(form.value.endTime).toISOString(),
    teamRegistrationAutoApprove: selectedComp.value?.teamRegistrationAutoApprove ?? true,
    maxTeamMembers: selectedComp.value?.maxTeamMembers ?? 5,
    maxConcurrentRuntimeInstancesPerTeam:
      selectedComp.value?.maxConcurrentRuntimeInstancesPerTeam ?? 0,
  }
}

const { data: competitions, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: async () => (await competitionAdminApi.list()).map(toCompetitionAdminDto),
})

const saveMutation = useMutation({
  mutationFn: async () => {
    if (!canSave.value) {
      throw new Error('invalid_competition_form')
    }
    const body = competitionPayload()
    if (isCreating.value) {
      await competitionAdminApi.create({
        ...body,
        mode: competitionMode(form.value.gameModeType),
      })
    }
    else {
      await competitionAdminApi.update(selectedComp.value!.id, body)
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

const archiveMutation = useMutation({
  mutationFn: async (id: string) => {
    await competitionAdminApi.archive(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    archiveDialog.value = false
    toast.success(t('admin.competitions.archiveSuccess'))
  },
  onError: () => {
    toast.error(t('admin.competitions.archiveError'))
  },
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => {
    await competitionAdminApi.hardDelete(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    deleteDialog.value = false
    toast.success(t('admin.competitions.deleteSuccess'))
  },
  onError: () => {
    toast.error(t('admin.competitions.deleteError'))
  },
})

function openCreate() {
  isCreating.value = true
  selectedComp.value = null
  form.value = { title: '', description: '', gameModeType: 'Ctf', status: 'Draft', ...defaultSchedule() }
  editDialog.value = true
}

function updateForm(nextForm: typeof form.value) {
  form.value = nextForm
}

function openDelete(comp: CompetitionAdminDto) {
  selectedComp.value = comp
  deleteDialog.value = true
}

function openArchive(comp: CompetitionAdminDto) {
  selectedComp.value = comp
  archiveDialog.value = true
}

function archiveSelectedCompetition() {
  const competitionId = selectedComp.value?.id
  if (competitionId)
    archiveMutation.mutate(competitionId)
}

function deleteSelectedCompetition() {
  const competitionId = selectedComp.value?.id
  if (competitionId)
    deleteMutation.mutate(competitionId)
}

function goPermissions(comp: CompetitionAdminDto) {
  router.push({
    name: 'admin-competition-detail',
    params: { id: comp.id },
    query: { section: 'permissions' },
  })
}

function goManage(comp: CompetitionAdminDto) {
  router.push({ name: 'admin-competition-detail', params: { id: comp.id } })
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'active')
    return 'default'
  if (s === 'published')
    return 'secondary'
  if (s === 'finished' || s === 'ended')
    return 'outline'
  if (s === 'draft')
    return 'outline'
  return 'secondary'
}

const columnHelper = createColumnHelper<CompetitionAdminDto>()

const columns = [
  columnHelper.accessor('title', {
    header: t('admin.competitions.titleColumn'),
    enableSorting: true,
    cell: info => info.getValue(),
  }),
  columnHelper.accessor('gameModeType', {
    header: t('admin.competitions.mode'),
    enableSorting: true,
    cell: info => h(Badge, { variant: 'outline', class: 'font-mono' }, () => info.getValue()),
  }),
  columnHelper.accessor('status', {
    header: t('admin.competitions.status'),
    cell: info => h(Badge, { variant: statusVariant(info.getValue()) }, () => info.getValue()),
  }),
  columnHelper.accessor('startTime', {
    header: t('admin.competitions.start'),
    cell: info => new Date(info.getValue()).toLocaleDateString(),
  }),
  columnHelper.accessor('endTime', {
    header: t('admin.competitions.end'),
    cell: info => new Date(info.getValue()).toLocaleDateString(),
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
    <AdminCompetitionsToolbar
      v-model:global-filter="globalFilter"
      @create="openCreate"
    />

    <Card class="p-0 overflow-hidden">
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
            <TableHead class="w-[80px] text-right px-4">
              {{ t('common.actions') }}
            </TableHead>
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
          <TableRow v-else-if="isError">
            <TableCell :colspan="columns.length + 1" class="h-32 text-center">
              <div class="flex flex-col items-center justify-center gap-3 text-muted-foreground">
                <span>{{ t('admin.competitions.loadError', t('errors.loadFailed')) }}</span>
                <Button variant="outline" size="sm" @click="refetch()">
                  {{ t('common.refresh') }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.competitions.empty') }}
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
                  <Button variant="ghost" size="icon" class="size-8 h-8 w-8 p-0">
                    <span class="sr-only">{{ t('common.actions') }}</span>
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[160px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem @click="goManage(row.original)">
                    <Settings class="mr-2 size-4" />
                    {{ t('admin.competitions.manage') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem @click="goPermissions(row.original)">
                    <ShieldCheck class="mr-2 size-4" />
                    {{ t('admin.competitions.permissions') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @click="router.push(`/competitions/${row.original.id}`)">
                    <ExternalLink class="mr-2 size-4" />
                    {{ t('admin.competitions.viewPublic') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem
                    v-if="row.original.status !== 'Running' && row.original.status !== 'Paused'"
                    @select="openArchive(row.original)"
                  >
                    <Archive class="mr-2 size-4" />
                    {{ t('admin.competitions.archive') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    v-if="row.original.status === 'Finished'"
                    class="text-destructive focus:text-destructive"
                    @select="openDelete(row.original)"
                  >
                    <Trash2 class="mr-2 size-4" />
                    {{ t('admin.competitions.deletePermanently') }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <div class="flex flex-col gap-3 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
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

    <AdminCompetitionEditorDialog
      v-model:open="editDialog"
      :is-creating="isCreating"
      :form="form"
      :can-save="canSave"
      :saving="saveMutation.isPending.value"
      @update:form="updateForm"
      @save="saveMutation.mutate()"
    />

    <AdminCompetitionDeleteDialog
      v-model:open="archiveDialog"
      mode="archive"
      :title="selectedComp?.title"
      :deleting="archiveMutation.isPending.value"
      @confirm="archiveSelectedCompetition"
    />

    <AdminCompetitionDeleteDialog
      v-model:open="deleteDialog"
      mode="delete"
      :title="selectedComp?.title"
      :deleting="deleteMutation.isPending.value"
      @confirm="deleteSelectedCompetition"
    />
  </div>
</template>
