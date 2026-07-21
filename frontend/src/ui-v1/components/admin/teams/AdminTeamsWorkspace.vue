<script setup lang="ts">
import { ref, h, watch } from 'vue'
import { useI18n } from 'vue-i18n'
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
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Button } from '@/ui-v1/components/ui/button'
import { Input } from '@/ui-v1/components/ui/input'
import { Badge } from '@/ui-v1/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/ui-v1/components/ui/dropdown-menu'
import { Search, MoreHorizontal, Users, Trash2, Loader2, Trophy } from 'lucide-vue-next'
import { Card } from '@/ui-v1/components/ui/card'
import { toast } from 'vue-sonner'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import AdminTeamDisbandDialog from '@/ui-v1/components/admin/teams/AdminTeamDisbandDialog.vue'
import AdminTeamMembersDialog from '@/ui-v1/components/admin/teams/AdminTeamMembersDialog.vue'
import { useAdminTeamsPage, type AdminTeamDto } from '@/features/admin/useAdminTeamsPage'

const { t } = useI18n()

type TeamDto = AdminTeamDto

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const membersDialog = ref(false)
const disbandDialog = ref(false)
const selectedTeam = ref<TeamDto | null>(null)

const {
  teams,
  isLoading,
  isError,
  refetch,
  teamMembers,
  loadingMembers,
  membersError,
  loadMembers,
  disbandMutation: disbandTeamMutation,
} = useAdminTeamsPage()

const disbandMutation = useToastMutation<string>(disbandTeamMutation, {
  success: 'admin.teams.disbandSuccess',
  error: 'admin.teams.disbandError',
}, {
  onSuccess: () => {
    disbandDialog.value = false
  },
})

watch(membersError, (cause) => {
  if (cause)
    toast.error(t('admin.teams.loadMembersError'))
})

async function openMembersDialog(team: TeamDto) {
  selectedTeam.value = team
  membersDialog.value = true
  await loadMembers(team.id)
}

function openDisbandDialog(team: TeamDto) {
  selectedTeam.value = team
  disbandDialog.value = true
}

const columnHelper = createColumnHelper<TeamDto>()

const columns = [
  columnHelper.accessor('name', { 
    header: t('admin.teams.name'), 
    enableSorting: true,
    cell: (info) => info.getValue()
  }),
  columnHelper.accessor('captainName', { 
    header: t('admin.teams.captain'), 
    enableSorting: true 
  }),
  columnHelper.accessor('memberCount', { 
    header: t('admin.teams.members'), 
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'font-mono' }, () => info.getValue().toString())
  }),
  columnHelper.accessor('registrationStatus', {
    header: t('admin.teams.registrationStatus'),
    enableSorting: true,
    cell: (info) => h(Badge, { variant: info.getValue() === 'approved' ? 'default' : info.getValue() === 'rejected' ? 'destructive' : 'secondary' }, () => info.getValue() ?? 'pending')
  }),
  columnHelper.accessor('competitionTitle', { 
    header: t('admin.teams.competition'), 
    enableSorting: true,
    cell: (info) => h('div', { class: 'flex items-center gap-2' }, [
      h(Trophy, { class: 'size-3 text-muted-foreground' }),
      h('span', info.getValue())
    ])
  }),
]

const table = useVueTable({
  get data() { return teams.value ?? [] },
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
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.teams.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.teams.subtitle') }}</p>
      </div>
    </div>

    <Card class="grid gap-3 p-3 md:grid-cols-[minmax(0,24rem)]">
      <div class="relative w-full max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="globalFilter" :placeholder="t('admin.teams.searchPlaceholder')" class="pl-10" />
      </div>
    </Card>

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
                  <span>{{ header.column.columnDef.header as string }}</span>
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]">▼</span>
                </div>
              </template>
            </TableHead>
            <TableHead class="w-[80px] text-right px-4">{{ t('common.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center">
              <div class="flex items-center justify-center gap-2 text-muted-foreground">
                <Loader2 class="size-4 animate-spin" />
                <span>{{ t('admin.teams.loading') }}</span>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="isError">
            <TableCell :colspan="columns.length + 1" class="h-32 text-center">
              <div class="flex flex-col items-center justify-center gap-3 text-muted-foreground">
                <span>{{ t('admin.teams.loadError', t('errors.loadFailed')) }}</span>
                <Button variant="outline" size="sm" @click="refetch()">{{ t('common.refresh') }}</Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.teams.empty') }}
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
                    <span class="sr-only">{{ t('common.actions') }}</span>
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[160px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem @click="openMembersDialog(row.original)">
                    <Users class="mr-2 size-4" />
                    {{ t('admin.teams.members') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @click="openDisbandDialog(row.original)" class="text-destructive focus:text-destructive font-medium">
                    <Trash2 class="mr-2 size-4" />
                    {{ t('admin.teams.disband') }}
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
        <Button variant="outline" size="sm" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">
          {{ t('common.previous') }}
        </Button>
        <Button variant="outline" size="sm" :disabled="!table.getCanNextPage()" @click="table.nextPage()">
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

    <AdminTeamMembersDialog
      v-model:open="membersDialog"
      :team="selectedTeam"
      :team-members="teamMembers"
      :loading="loadingMembers"
    />

    <AdminTeamDisbandDialog
      v-model:open="disbandDialog"
      :team="selectedTeam"
      :deleting="disbandMutation.isPending.value"
      @confirm="disbandMutation.mutate(selectedTeam!.id)"
    />
  </div>
</template>
