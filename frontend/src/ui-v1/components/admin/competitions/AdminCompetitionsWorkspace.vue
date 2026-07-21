<script setup lang="ts">
import type { SortingState } from '@tanstack/vue-table'
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
  ExternalLink,
  Loader2,
  MoreHorizontal,
  Settings,
  Trash2,
  Users2,
} from 'lucide-vue-next'
import { h, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/ui-v1/components/ui/dropdown-menu'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'
import AdminCompetitionDeleteDialog from '@/ui-v1/components/admin/competitions/AdminCompetitionDeleteDialog.vue'
import AdminCompetitionEditorDialog from '@/ui-v1/components/admin/competitions/AdminCompetitionEditorDialog.vue'
import AdminCompetitionsToolbar from '@/ui-v1/components/admin/competitions/AdminCompetitionsToolbar.vue'
import { Card } from '@/ui-v1/components/ui/card'
import { useAdminCompetitionsPage, type AdminCompetitionDto, type AdminCompetitionForm } from '@/features/admin/useAdminCompetitionsPage'

const { t } = useI18n()

type CompetitionAdminDto = AdminCompetitionDto

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)

const {
  competitions,
  isLoading,
  isError,
  refetch,
  form,
  isCreating,
  selectedCompetition: selectedComp,
  canSave,
  saveMutation: save,
  deleteMutation: removeCompetition,
  prepareCreate,
  goCollaborators,
  goManage,
  goViewPublic,
  goAwdpScreen,
} = useAdminCompetitionsPage()

const saveMutation = useToastMutation(save, {
  success: () => isCreating.value ? 'admin.competitions.createSuccess' : 'admin.competitions.updateSuccess',
  error: 'admin.competitions.saveError',
}, {
  onSuccess: () => {
    editDialog.value = false
  },
})

const deleteMutation = useToastMutation<string>(removeCompetition, {
  success: 'admin.competitions.deleteSuccess',
  error: 'admin.competitions.deleteError',
}, {
  onSuccess: () => {
    deleteDialog.value = false
  },
})

function openCreate() {
  prepareCreate()
  editDialog.value = true
}

function updateForm(nextForm: AdminCompetitionForm) {
  form.value = nextForm
}

function openDelete(comp: CompetitionAdminDto) {
  selectedComp.value = comp
  deleteDialog.value = true
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
                  <DropdownMenuItem @click="goCollaborators(row.original)">
                    <Users2 class="mr-2 size-4" />
                    {{ t('admin.competitions.collaborators') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @click="goViewPublic(row.original.id)">
                    <ExternalLink class="mr-2 size-4" />
                    {{ t('admin.competitions.viewPublic') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    v-if="row.original.gameModeType.toLowerCase() === 'awdp'"
                    @click="goAwdpScreen(row.original.id)"
                  >
                    <ExternalLink class="mr-2 size-4" />
                    {{ t('awdp.screenEntry') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem class="text-destructive focus:text-destructive" @click="openDelete(row.original)">
                    <Trash2 class="mr-2 size-4" />
                    {{ t('common.delete') }}
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
      v-model:open="deleteDialog"
      :title="selectedComp?.title"
      :deleting="deleteMutation.isPending.value"
      @confirm="deleteMutation.mutate(selectedComp!.id)"
    />
  </div>
</template>
