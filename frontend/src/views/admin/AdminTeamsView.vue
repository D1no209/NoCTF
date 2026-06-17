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
import { client } from '@/api/generated/client.gen'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
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

const { t } = useI18n()
const qc = useQueryClient()

interface TeamDto {
  id: string
  name: string
  captainName: string
  memberCount: number
  competitionTitle: string
}

interface TeamMemberDto {
  userId: string
  userName: string
  role: string
}

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const membersDialog = ref(false)
const disbandDialog = ref(false)
const selectedTeam = ref<TeamDto | null>(null)
const teamMembers = ref<TeamMemberDto[]>([])
const loadingMembers = ref(false)

const { data: teams, isLoading } = useQuery({
  queryKey: ['admin-teams'],
  queryFn: async () => {
    const res = await client.get<{ 200: TeamDto[] }, unknown, false>({ url: '/api/admin/teams' })
    return res.data ?? []
  },
})

const disbandMutation = useMutation({
  mutationFn: async (id: string) => {
    await client.delete({ url: `/api/admin/teams/${id}` })
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['admin-teams'] })
    disbandDialog.value = false
  },
})

async function openMembersDialog(team: TeamDto) {
  selectedTeam.value = team
  loadingMembers.value = true
  membersDialog.value = true
  try {
    const res = await client.get<{ 200: TeamMemberDto[] }, unknown, false>({
      url: `/api/admin/teams/${team.id}/members`,
    })
    teamMembers.value = res.data ?? []
  } finally {
    loadingMembers.value = false
  }
}

function openDisbandDialog(team: TeamDto) {
  selectedTeam.value = team
  disbandDialog.value = true
}

const columnHelper = createColumnHelper<TeamDto>()

const columns = [
  columnHelper.accessor('name', { header: t('admin.teams.name'), enableSorting: true }),
  columnHelper.accessor('captainName', { header: t('admin.teams.captain'), enableSorting: true }),
  columnHelper.accessor('memberCount', { header: t('admin.teams.members'), enableSorting: true }),
  columnHelper.accessor('competitionTitle', { header: t('admin.teams.competition'), enableSorting: true }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => {
      const team = info.row.original
      return h('div', { class: 'flex gap-2' }, [
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openMembersDialog(team) }, () => t('admin.teams.members')),
        h(Button, { size: 'sm', variant: 'destructive', onClick: () => openDisbandDialog(team) }, () => t('admin.teams.disband')),
      ])
    },
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
  <div class="p-6 space-y-4">
    <h1 class="text-2xl font-bold">{{ t('admin.teams.title') }}</h1>

    <Input v-model="globalFilter" :placeholder="t('admin.teams.searchPlaceholder')" class="max-w-xs" />

    <div class="rounded-md border">
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
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.teams.loading') }}</TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.teams.empty') }}</TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
              <component :is="() => cell.renderValue()" v-if="cell.column.id === 'actions'" />
              <template v-else>{{ cell.getValue() }}</template>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div class="flex items-center justify-between">
      <span class="text-sm text-muted-foreground">
        {{ t('common.pageOf', { page: table.getState().pagination.pageIndex + 1, total: table.getPageCount() }) }}
      </span>
      <div class="flex gap-2">
        <Button size="sm" variant="outline" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">{{ t('common.previous') }}</Button>
        <Button size="sm" variant="outline" :disabled="!table.getCanNextPage()" @click="table.nextPage()">{{ t('common.next') }}</Button>
      </div>
    </div>

    <!-- Members Dialog -->
    <Dialog v-model:open="membersDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.teams.membersDialog', { name: selectedTeam?.name }) }}</DialogTitle>
        </DialogHeader>
        <div class="py-2">
          <div v-if="loadingMembers" class="text-muted-foreground text-sm">{{ t('common.loading') }}</div>
          <div v-else-if="teamMembers.length === 0" class="text-muted-foreground text-sm">{{ t('admin.teams.noMembers') }}</div>
          <ul v-else class="space-y-1">
            <li v-for="m in teamMembers" :key="m.userId" class="flex items-center justify-between text-sm py-1 border-b last:border-0">
              <span>{{ m.userName }}</span>
              <span class="text-muted-foreground capitalize">{{ t('admin.teams.memberRole', { role: m.role }) }}</span>
            </li>
          </ul>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="membersDialog = false">{{ t('common.close') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Disband Confirmation Dialog -->
    <Dialog v-model:open="disbandDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.teams.disbandDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm py-2" v-html="t('admin.teams.disbandConfirm', { name: selectedTeam?.name })"></p>
        <DialogFooter>
          <Button variant="outline" @click="disbandDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="disbandMutation.isPending.value"
            @click="disbandMutation.mutate(selectedTeam!.id)"
          >
            {{ t('admin.teams.disband') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
