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
import { Label } from '@/components/ui/label'
import { Search, UserPlus, UserMinus, ShieldCheck, Loader2, Trophy, Users } from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import { vAutoAnimate } from '@formkit/auto-animate/vue'

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

// Load competitions for dropdown
const { data: competitions } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: () => adminApi.competitions<CompetitionOption[]>(),
})

// Load collaborators for selected competition
const {
  data: collaborators,
  isLoading,
  isError: isCollaboratorsError,
  refetch: refetchCollaborators,
} = useQuery({
  queryKey: computed(() => queryKeys.adminCollaborators(selectedCompetitionId.value)),
  queryFn: async () => {
    if (!selectedCompetitionId.value) return []
    return adminApi.collaborators<CollaboratorDto[]>(selectedCompetitionId.value)
  },
  enabled: computed(() => !!selectedCompetitionId.value),
})

// User search for add dialog
const { data: userSearchResults } = useQuery({
  queryKey: computed(() => ['admin-user-search', newUserSearch.value]),
  queryFn: async () => {
    if (!newUserSearch.value || newUserSearch.value.length < 2) return []
    const all = await adminApi.users<{ id: string; userName: string }[]>()
    return all.filter((u) => u.userName.toLowerCase().includes(newUserSearch.value.toLowerCase()))
  },
  enabled: () => newUserSearch.value.length >= 2,
})

const addMutation = useMutation({
  mutationFn: async () => {
    await adminApi.addCollaborator(selectedCompetitionId.value, {
      userId: newUserId.value,
      role: newRole.value,
    })
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    addDialog.value = false
    newUserId.value = ''
    newUserSearch.value = ''
    toast.success(t('admin.collaborators.addSuccess'))
  },
  onError: () => {
    toast.error(t('admin.collaborators.addError'))
  },
})

const removeMutation = useMutation({
  mutationFn: async (userId: string) => {
    await adminApi.removeCollaborator(selectedCompetitionId.value, userId)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    removeDialog.value = false
    toast.success(t('admin.collaborators.removeSuccess'))
  },
  onError: () => {
    toast.error(t('admin.collaborators.removeError'))
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

watch(
  () => route.query.competitionId,
  (id) => {
    if (id) {
      selectedCompetitionId.value = id as string
      selectedCompetitionTitle.value = (route.query.competitionTitle as string) ?? ''
    }
  },
)

const columnHelper = createColumnHelper<CollaboratorDto>()

const columns = [
  columnHelper.accessor('userName', {
    header: t('admin.collaborators.username'),
    cell: (info) => h('span', { class: 'font-medium' }, info.getValue()),
  }),
  columnHelper.accessor('role', {
    header: t('admin.collaborators.role'),
    cell: (info) =>
      h(
        Badge,
        {
          variant: info.getValue().toLowerCase() === 'manager' ? 'default' : 'secondary',
          class: 'capitalize',
        },
        () => info.getValue(),
      ),
  }),
]

const table = useVueTable({
  get data() {
    return collaborators.value ?? []
  },
  columns,
  getCoreRowModel: getCoreRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
})
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.collaborators.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.collaborators.subtitle') }}</p>
      </div>
    </div>

    <!-- Competition selector -->
    <div class="noctf-filter-bar sm:grid-cols-[minmax(0,28rem)_auto] sm:items-center">
      <div class="flex w-full max-w-md items-center gap-3">
        <Trophy class="size-4 text-muted-foreground shrink-0" />
        <Select v-model="selectedCompetitionId">
          <SelectTrigger>
            <SelectValue :placeholder="t('admin.collaborators.selectCompetition')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="c in competitions" :key="c.id" :value="c.id">{{
              c.title
            }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <Button v-if="selectedCompetitionId" @click="addDialog = true" class="w-full sm:w-auto">
        <UserPlus class="mr-2 size-4" />
        {{ t('admin.collaborators.addCollaborator') }}
      </Button>
    </div>

    <template v-if="selectedCompetitionId">
      <div class="noctf-table-shell">
        <Table>
          <TableHeader>
            <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
              <TableHead v-for="header in headerGroup.headers" :key="header.id" class="px-4 py-3">
                <template v-if="!header.isPlaceholder">{{
                  header.column.columnDef.header as string
                }}</template>
              </TableHead>
              <TableHead class="w-[80px] text-right px-4">{{ t('common.actions') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody v-auto-animate>
            <TableRow v-if="isLoading">
              <TableCell :colspan="columns.length + 1" class="h-24 text-center">
                <Loader2 class="size-4 animate-spin mx-auto text-muted-foreground" />
              </TableCell>
            </TableRow>
            <TableRow v-else-if="isCollaboratorsError">
              <TableCell
                :colspan="columns.length + 1"
                class="h-24 text-center text-sm text-muted-foreground"
              >
                <div class="flex flex-col items-center gap-3">
                  <span>{{ t('admin.collaborators.loadError') }}</span>
                  <Button size="sm" variant="outline" @click="refetchCollaborators()">
                    {{ t('common.refresh') }}
                  </Button>
                </div>
              </TableCell>
            </TableRow>
            <TableRow v-else-if="table.getRowModel().rows.length === 0">
              <TableCell
                :colspan="columns.length + 1"
                class="h-24 text-center text-muted-foreground text-sm"
              >
                {{ t('admin.collaborators.empty') }}
              </TableCell>
            </TableRow>
            <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
              <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
                <component :is="() => cell.renderValue()" />
              </TableCell>
              <TableCell class="px-4 py-3 text-right">
                <Button
                  variant="ghost"
                  size="icon"
                  class="text-destructive hover:text-destructive hover:bg-destructive/10"
                  @click="openRemove(row.original)"
                >
                  <UserMinus class="size-4" />
                </Button>
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
        <div class="flex gap-2">
          <Button
            size="sm"
            variant="outline"
            :disabled="!table.getCanPreviousPage()"
            @click="table.previousPage()"
            >{{ t('common.previous') }}</Button
          >
          <Button
            size="sm"
            variant="outline"
            :disabled="!table.getCanNextPage()"
            @click="table.nextPage()"
            >{{ t('common.next') }}</Button
          >
        </div>
      </div>
    </template>

    <div v-else class="noctf-state-box py-20">
      <div
        class="size-12 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4"
      >
        <Users class="size-6" />
      </div>
      <h3 class="text-lg font-medium">{{ t('admin.collaborators.noCompetitionSelected') }}</h3>
      <p class="text-sm text-muted-foreground mt-1">{{ t('admin.collaborators.selectPrompt') }}</p>
    </div>

    <!-- Add Collaborator Dialog -->
    <Dialog v-model:open="addDialog">
      <DialogContent class="sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{{ t('admin.collaborators.addDialogTitle') }}</DialogTitle>
          <DialogDescription>{{ t('admin.collaborators.addDialogDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4">
          <div class="space-y-2">
            <Label>{{ t('admin.collaborators.searchUser') }}</Label>
            <div class="relative">
              <Search
                class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
              />
              <Input
                v-model="newUserSearch"
                :placeholder="t('admin.collaborators.typeUsername')"
                class="pl-10"
              />
            </div>

            <div
              v-if="newUserSearch.length >= 2"
              v-auto-animate
              class="mt-2 border rounded-lg overflow-hidden bg-muted/20"
            >
              <div
                v-if="!userSearchResults || userSearchResults.length === 0"
                class="p-3 text-center text-xs text-muted-foreground"
              >
                {{ t('admin.collaborators.noUsersFound') }}
              </div>
              <div
                v-for="u in userSearchResults"
                :key="u.id"
                class="flex items-center justify-between px-3 py-2 text-sm cursor-pointer hover:bg-accent transition-colors"
                :class="newUserId === u.id ? 'bg-accent font-bold' : ''"
                @click="selectUser(u)"
              >
                <span>{{ u.userName }}</span>
                <ShieldCheck v-if="newUserId === u.id" class="size-4 text-primary" />
              </div>
            </div>
          </div>

          <div class="space-y-2">
            <Label>{{ t('admin.collaborators.role') }}</Label>
            <Select v-model="newRole">
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Manager">{{ t('admin.collaborators.roleManager') }}</SelectItem>
                <SelectItem value="Observer">{{
                  t('admin.collaborators.roleObserver')
                }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="addDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            :disabled="addMutation.isPending.value || !newUserId"
            @click="addMutation.mutate()"
          >
            <Loader2 v-if="addMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('common.add') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Remove Confirmation -->
    <Dialog v-model:open="removeDialog">
      <DialogContent class="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle class="text-destructive">{{
            t('admin.collaborators.removeDialogTitle')
          }}</DialogTitle>
          <DialogDescription>{{
            t('admin.collaborators.removeDialogDescription')
          }}</DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <p class="text-sm font-medium">
            {{ t('admin.collaborators.removeQuestion') }}
            <span class="font-bold underline">{{ selectedCollab?.userName }}</span
            >?
          </p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="removeDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="removeMutation.isPending.value"
            @click="removeMutation.mutate(selectedCollab!.userId)"
          >
            <Loader2 v-if="removeMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('admin.collaborators.remove') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
