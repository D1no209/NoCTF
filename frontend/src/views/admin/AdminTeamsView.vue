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
import { Search, MoreHorizontal, Users, Trash2, Shield, User as UserIcon, Loader2, Trophy } from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import { vAutoAnimate } from '@formkit/auto-animate/vue'

const { t } = useI18n()
const qc = useQueryClient()

interface TeamDto {
  id: string
  competitionId: string
  name: string
  captainName: string
  memberCount: number
  competitionTitle: string
  inviteToken?: string
  isLocked?: boolean
  registrationStatus?: string
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
  queryKey: queryKeys.adminTeams,
  queryFn: () => adminApi.teams<TeamDto[]>(),
})

const disbandMutation = useMutation({
  mutationFn: async (id: string) => {
    await adminApi.deleteTeam(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminTeams })
    disbandDialog.value = false
    toast.success(t('admin.teams.disbandSuccess', 'Team disbanded successfully.'))
  },
  onError: () => {
    toast.error(t('admin.teams.disbandError', 'Failed to disband team.'))
  }
})

async function openMembersDialog(team: TeamDto) {
  selectedTeam.value = team
  loadingMembers.value = true
  membersDialog.value = true
  try {
    teamMembers.value = await adminApi.teamMembers<TeamMemberDto[]>(team.id)
  } catch {
    toast.error(t('admin.teams.loadMembersError'))
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
        <p class="text-sm text-muted-foreground">Monitor and manage registered teams across all competitions.</p>
      </div>
    </div>

    <div class="flex items-center gap-2">
      <div class="relative w-full max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="globalFilter" :placeholder="t('admin.teams.searchPlaceholder')" class="pl-10" />
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
                    <span class="sr-only">Open menu</span>
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
    </div>

    <div class="flex items-center justify-between">
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

    <!-- Members Dialog -->
    <Dialog v-model:open="membersDialog">
      <DialogContent class="sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{{ t('admin.teams.membersDialog', { name: selectedTeam?.name }) }}</DialogTitle>
          <DialogDescription>Overview of the current team roster.</DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <div v-if="loadingMembers" class="flex items-center justify-center p-8 text-muted-foreground">
            <Loader2 class="size-6 animate-spin mr-3" /> {{ t('common.loading') }}
          </div>
          <div v-else-if="teamMembers.length === 0" class="text-center p-8 text-muted-foreground text-sm border rounded-lg border-dashed">
            {{ t('admin.teams.noMembers') }}
          </div>
          <ul v-else class="space-y-2">
            <li 
              v-for="m in teamMembers" 
              :key="m.userId" 
              class="flex items-center justify-between p-3 rounded-xl border bg-muted/30 transition-all hover:bg-muted/50"
            >
              <div class="flex items-center gap-3">
                <div class="flex size-8 items-center justify-center rounded-full bg-background shadow-sm border">
                  <Shield v-if="m.role.toLowerCase() === 'captain'" class="size-4 text-primary" />
                  <UserIcon v-else class="size-4 text-muted-foreground" />
                </div>
                <div class="flex flex-col">
                  <span class="font-semibold text-sm leading-none">{{ m.userName }}</span>
                  <span class="text-[10px] text-muted-foreground mt-1">{{ m.userId.slice(0, 8) }}</span>
                </div>
              </div>
              <Badge :variant="m.role.toLowerCase() === 'captain' ? 'default' : 'secondary'" class="capitalize text-[10px] font-bold tracking-tighter">
                {{ m.role }}
              </Badge>
            </li>
          </ul>
        </div>
        <DialogFooter>
          <Button variant="outline" class="w-full sm:w-auto" @click="membersDialog = false">{{ t('common.close') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Disband Confirmation -->
    <Dialog v-model:open="disbandDialog">
      <DialogContent class="sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle class="text-destructive flex items-center gap-2">
            <Trash2 class="size-5" />
            {{ t('admin.teams.disbandDialogTitle') }}
          </DialogTitle>
          <DialogDescription>
            This action is irreversible. The team's data will be permanently removed.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <div class="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-sm">
            <p class="font-medium">Confirm disbanding <span class="font-bold underline">{{ selectedTeam?.name }}</span>?</p>
          </div>
        </div>
        <DialogFooter class="gap-2">
          <Button variant="outline" @click="disbandDialog = false" :disabled="disbandMutation.isPending.value">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="disbandMutation.isPending.value"
            @click="disbandMutation.mutate(selectedTeam!.id)"
          >
            <Loader2 v-if="disbandMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('admin.teams.disband') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
