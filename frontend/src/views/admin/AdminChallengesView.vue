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
import { Textarea } from '@/components/ui/textarea'
import { Alert } from '@/components/ui/alert'
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

interface CheckerConfigDto {
  image?: string
  command?: string
}

interface PointsConfigDto {
  initialPoints: number
  minimumPoints: number
}

interface ChallengeAdminDto {
  id: string
  competitionId: string
  title: string
  description?: string
  typeId: string
  containerImage?: string
  containerMode: 'SingleImage' | 'DockerCompose' | number
  composeYaml?: string
  composeProjectName?: string
  attachmentUrl?: string
  checkerConfig?: CheckerConfigDto
  pointsConfig: PointsConfigDto
}

interface CompetitionOption {
  id: string
  title: string
}

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const selectedChallenge = ref<ChallengeAdminDto | null>(null)
const isCreating = ref(false)
const actionError = ref('')
const revealDialog = ref(false)
const revealedSecret = ref('')

const defaultForm = () => ({
  competitionId: '',
  title: '',
  description: '',
  typeId: 'ctf',
  containerImage: '',
  containerMode: 'SingleImage',
  composeYaml: '',
  composeProjectName: '',
  flagSecret: '',
  attachmentUrl: '',
  checkerImage: '',
  checkerCommand: '',
  initialPoints: 500,
  minimumPoints: 100,
})

const form = ref(defaultForm())

const { data: challenges, isLoading } = useQuery({
  queryKey: queryKeys.adminChallenges,
  queryFn: () => adminApi.challenges<ChallengeAdminDto[]>(),
})

const { data: competitions } = useQuery({
  queryKey: queryKeys.adminCompetitions,
  queryFn: () => adminApi.competitions<CompetitionOption[]>(),
})

const saveMutation = useMutation({
  mutationFn: async () => {
    const body = {
      competitionId: form.value.competitionId || undefined,
      title: form.value.title,
      description: form.value.description || undefined,
      typeId: form.value.typeId,
      containerImage: form.value.containerImage || undefined,
      containerMode: form.value.containerMode === 'DockerCompose' ? 1 : 0,
      composeYaml: form.value.composeYaml || undefined,
      composeProjectName: form.value.composeProjectName || undefined,
      flagSecret: form.value.flagSecret || undefined,
      attachmentUrl: form.value.attachmentUrl || undefined,
      checkerConfig: (form.value.checkerImage || form.value.checkerCommand)
        ? { image: form.value.checkerImage || undefined, command: form.value.checkerCommand || undefined }
        : undefined,
      pointsConfig: { initialPoints: form.value.initialPoints, minimumPoints: form.value.minimumPoints },
    }
    if (isCreating.value) {
      await adminApi.createChallenge(body)
    } else {
      await adminApi.updateChallenge(selectedChallenge.value!.id, body)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    editDialog.value = false
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.challenges.saveError') },
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => {
    await adminApi.deleteChallenge(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    deleteDialog.value = false
  },
})

const revealMutation = useMutation({
  mutationFn: async (id: string) => adminApi.revealChallengeSecret<{ flagSecret?: string; secret?: string }>(id),
  onSuccess: (data) => {
    revealedSecret.value = data.flagSecret ?? data.secret ?? ''
    actionError.value = ''
  },
  onError: () => { actionError.value = t('admin.challenges.revealError') },
})

function openCreate() {
  isCreating.value = true
  selectedChallenge.value = null
  form.value = defaultForm()
  actionError.value = ''
  editDialog.value = true
}

function openEdit(c: ChallengeAdminDto) {
  isCreating.value = false
  selectedChallenge.value = c
  form.value = {
    competitionId: c.competitionId,
    title: c.title,
    description: c.description ?? '',
    typeId: c.typeId,
    containerImage: c.containerImage ?? '',
    containerMode: c.containerMode === 1 || c.containerMode === 'DockerCompose' ? 'DockerCompose' : 'SingleImage',
    composeYaml: c.composeYaml ?? '',
    composeProjectName: c.composeProjectName ?? '',
    flagSecret: '',
    attachmentUrl: c.attachmentUrl ?? '',
    checkerImage: c.checkerConfig?.image ?? '',
    checkerCommand: c.checkerConfig?.command ?? '',
    initialPoints: c.pointsConfig?.initialPoints ?? 500,
    minimumPoints: c.pointsConfig?.minimumPoints ?? 100,
  }
  actionError.value = ''
  editDialog.value = true
}

function openDelete(c: ChallengeAdminDto) {
  selectedChallenge.value = c
  deleteDialog.value = true
}

function openReveal(c: ChallengeAdminDto) {
  selectedChallenge.value = c
  revealedSecret.value = ''
  actionError.value = ''
  revealDialog.value = true
}

const columnHelper = createColumnHelper<ChallengeAdminDto>()

const columns = [
  columnHelper.accessor('title', { header: t('admin.challenges.titleColumn'), enableSorting: true }),
  columnHelper.accessor('typeId', { header: t('admin.challenges.type'), enableSorting: true }),
  columnHelper.accessor('containerImage', {
    header: t('admin.challenges.containerImage'),
    cell: (info) => info.getValue() ?? '—',
  }),
  columnHelper.accessor('checkerConfig', {
    header: t('admin.challenges.checkerImage'),
    cell: (info) => info.getValue()?.image ?? '—',
  }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => {
      const c = info.row.original
      return h('div', { class: 'flex gap-1' }, [
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openEdit(c) }, () => t('common.edit')),
        h(Button, { size: 'sm', variant: 'outline', onClick: () => openReveal(c) }, () => t('admin.challenges.revealSecret')),
        h(Button, { size: 'sm', variant: 'destructive', onClick: () => openDelete(c) }, () => t('common.delete')),
      ])
    },
  }),
]

const table = useVueTable({
  get data() { return challenges.value ?? [] },
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
    <PageHeader :title="t('admin.challenges.title')">
      <template #actions>
        <Button @click="openCreate">{{ t('admin.challenges.create') }}</Button>
      </template>
    </PageHeader>

    <Input v-model="globalFilter" :placeholder="t('admin.challenges.searchPlaceholder')" class="max-w-xs" />

    <ResponsiveTableShell dense min-width="920px">
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
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.challenges.loading') }}</TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.challenges.empty') }}</TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
              <component :is="() => cell.renderValue()" v-if="['actions', 'containerImage', 'checkerConfig'].includes(cell.column.id)" />
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
      <DialogContent class="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? t('admin.challenges.createDialogTitle') : t('admin.challenges.editDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <div>
            <Label>{{ t('admin.challenges.competition') }}</Label>
            <Select v-model="form.competitionId" class="mt-1">
              <option disabled value="">{{ t('admin.challenges.selectCompetition') }}</option>
              <option v-for="comp in competitions" :key="comp.id" :value="comp.id">{{ comp.title }}</option>
            </Select>
          </div>
          <div>
            <Label>{{ t('admin.challenges.titleColumn') }}</Label>
            <Input v-model="form.title" :placeholder="t('admin.challenges.titleColumn')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.challenges.description') }}</Label>
            <Textarea
              v-model="form.description"
              :placeholder="t('admin.challenges.optionalDescription')"
              rows="3"
              class="mt-1 resize-none"
            />
          </div>
          <div>
            <Label>{{ t('admin.challenges.type') }}</Label>
            <Select v-model="form.typeId" class="mt-1">
              <option value="ctf">CTF</option>
              <option value="awd">AWD</option>
              <option value="awdp">AWDP</option>
              <option value="koh">KoH</option>
            </Select>
          </div>
          <div>
            <Label>{{ t('admin.challenges.flagSecret') }}</Label>
            <Input v-model="form.flagSecret" :placeholder="t('admin.challenges.flagSecretPlaceholder')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.challenges.containerImage') }}</Label>
            <Input v-model="form.containerImage" :placeholder="t('admin.challenges.containerImagePlaceholder')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.challenges.containerMode') }}</Label>
            <Select v-model="form.containerMode" class="mt-1">
              <option value="SingleImage">{{ t('admin.challenges.singleImage') }}</option>
              <option value="DockerCompose">{{ t('admin.challenges.dockerCompose') }}</option>
            </Select>
          </div>
          <div v-if="form.containerMode === 'DockerCompose'">
            <Label>{{ t('admin.challenges.composeProjectName') }}</Label>
            <Input v-model="form.composeProjectName" :placeholder="t('admin.challenges.composeProjectNamePlaceholder')" class="mt-1" />
          </div>
          <div v-if="form.containerMode === 'DockerCompose'" class="md:col-span-2">
            <Label>{{ t('admin.challenges.composeYaml') }}</Label>
            <Textarea
              v-model="form.composeYaml"
              :placeholder="t('admin.challenges.composeYamlPlaceholder')"
              rows="10"
              class="mt-1 resize-y font-mono"
            />
            <p class="mt-1 text-xs text-muted-foreground">{{ t('admin.challenges.composeYamlHelp') }}</p>
          </div>
          <div>
            <Label>{{ t('admin.challenges.checkerImage') }}</Label>
            <Input v-model="form.checkerImage" :placeholder="t('admin.challenges.checkerImagePlaceholder')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.challenges.checkerCommand') }}</Label>
            <Input v-model="form.checkerCommand" :placeholder="t('admin.challenges.checkerCommandPlaceholder')" class="mt-1" />
          </div>
          <div>
            <Label>{{ t('admin.challenges.attachmentUrl') }}</Label>
            <Input v-model="form.attachmentUrl" :placeholder="t('admin.challenges.attachmentUrlPlaceholder')" class="mt-1" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <Label>{{ t('admin.challenges.initialPoints') }}</Label>
              <Input v-model.number="form.initialPoints" type="number" min="0" class="mt-1" />
            </div>
            <div>
              <Label>{{ t('admin.challenges.minPoints') }}</Label>
              <Input v-model.number="form.minimumPoints" type="number" min="0" class="mt-1" />
            </div>
          </div>
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="editDialog = false">{{ t('common.cancel') }}</Button>
          <Button :disabled="saveMutation.isPending.value || !form.title || !form.competitionId" @click="saveMutation.mutate()">
            {{ isCreating ? t('common.create') : t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Delete Confirmation -->
    <Dialog v-model:open="deleteDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.deleteDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm py-2" v-html="t('admin.challenges.deleteConfirm', { title: selectedChallenge?.title })"></p>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="deleteMutation.isPending.value"
            @click="deleteMutation.mutate(selectedChallenge!.id)"
          >
            {{ t('common.delete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Reveal Secret Confirmation -->
    <Dialog v-model:open="revealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.revealSecret') }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-3 py-2">
          <p class="text-sm text-muted-foreground">
            {{ t('admin.challenges.revealSecretAuditHint') }}
          </p>
          <Alert v-if="revealedSecret" class="font-mono break-all">
            {{ revealedSecret }}
          </Alert>
          <p v-if="actionError" class="text-sm text-destructive">{{ actionError }}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="revealDialog = false">{{ t('common.close') }}</Button>
          <Button
            variant="destructive"
            :disabled="revealMutation.isPending.value || !selectedChallenge"
            @click="revealMutation.mutate(selectedChallenge!.id)"
          >
            {{ revealedSecret ? t('common.refresh') : t('admin.challenges.revealSecret') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
