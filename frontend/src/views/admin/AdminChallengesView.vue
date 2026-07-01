<script setup lang="ts">
import { computed, ref, h, watch } from 'vue'
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
import { Textarea } from '@/components/ui/textarea'
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
import { 
  Plus, 
  Search, 
  MoreHorizontal, 
  Edit, 
  Key, 
  Trash2, 
  Loader2, 
  Box
} from 'lucide-vue-next'
import { toast } from 'vue-sonner'

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

const canSave = computed(() => Boolean(form.value.title.trim()) && Boolean(selectedCompetitionId()))

function selectedCompetitionId() {
  return form.value.competitionId || competitions.value?.[0]?.id || ''
}

watch(competitions, (items) => {
  if (isCreating.value && !form.value.competitionId && items?.length) {
    form.value.competitionId = items[0].id
  }
})

function challengePayload() {
  return {
    competitionId: selectedCompetitionId() || undefined,
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    typeId: form.value.typeId,
    containerImage: form.value.containerImage.trim() || undefined,
    containerMode: form.value.containerMode === 'DockerCompose' ? 1 : 0,
    composeYaml: form.value.composeYaml.trim() || undefined,
    composeProjectName: form.value.composeProjectName.trim() || undefined,
    flagSecret: form.value.flagSecret.trim() || undefined,
    attachmentUrl: form.value.attachmentUrl.trim() || undefined,
    checkerConfig: (form.value.checkerImage.trim() || form.value.checkerCommand.trim())
      ? { image: form.value.checkerImage.trim() || undefined, command: form.value.checkerCommand.trim() || undefined }
      : undefined,
    pointsConfig: {
      initialPoints: Number(form.value.initialPoints) || 500,
      minimumPoints: Number(form.value.minimumPoints) || 100,
    },
  }
}

const saveMutation = useMutation({
  mutationFn: async () => {
    if (!canSave.value) {
      throw new Error('invalid_challenge_form')
    }
    const body = challengePayload()
    if (isCreating.value) {
      await adminApi.createChallenge(body)
    } else {
      await adminApi.updateChallenge(selectedChallenge.value!.id, body)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    editDialog.value = false
    toast.success(isCreating.value ? t('admin.challenges.createSuccess') : t('admin.challenges.updateSuccess'))
  },
  onError: () => { toast.error(t('admin.challenges.saveError')) },
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => {
    await adminApi.deleteChallenge(id)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    deleteDialog.value = false
    toast.success(t('admin.challenges.deleteSuccess'))
  },
  onError: () => { toast.error(t('admin.challenges.deleteError')) }
})

const revealMutation = useMutation({
  mutationFn: async (id: string) => adminApi.revealChallengeSecret<{ flagSecret?: string; secret?: string }>(id),
  onSuccess: (data) => {
    revealedSecret.value = data.flagSecret ?? data.secret ?? ''
  },
  onError: () => { toast.error(t('admin.challenges.revealError')) },
})

function openCreate() {
  isCreating.value = true
  selectedChallenge.value = null
  form.value = defaultForm()
  form.value.competitionId = competitions.value?.[0]?.id ?? ''
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
  editDialog.value = true
}

function openDelete(c: ChallengeAdminDto) {
  selectedChallenge.value = c
  deleteDialog.value = true
}

function openReveal(c: ChallengeAdminDto) {
  selectedChallenge.value = c
  revealedSecret.value = ''
  revealDialog.value = true
}

const columnHelper = createColumnHelper<ChallengeAdminDto>()

const columns = [
  columnHelper.accessor('title', { 
    header: t('admin.challenges.titleColumn'), 
    enableSorting: true 
  }),
  columnHelper.accessor('typeId', { 
    header: t('admin.challenges.type'), 
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'uppercase font-bold text-[10px]' }, () => info.getValue())
  }),
  columnHelper.accessor('containerImage', {
    header: t('admin.challenges.containerImage'),
    cell: (info) => info.getValue() ? h('code', { class: 'text-xs bg-muted px-1 rounded' }, info.getValue()) : h('span', { class: 'text-muted-foreground' }, '-'),
  }),
  columnHelper.accessor('pointsConfig', {
    header: 'Points',
    cell: (info) => `${info.getValue().minimumPoints} → ${info.getValue().initialPoints}`
  })
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
  <div class="space-y-6">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.challenges.title') }}</h2>
        <p class="text-sm text-muted-foreground">Manage challenges and container configurations.</p>
      </div>
      <Button @click="openCreate">
        <Plus class="mr-2 size-4" />
        {{ t('admin.challenges.create') }}
      </Button>
    </div>

    <div class="flex items-center gap-2">
      <div class="relative w-full max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="globalFilter" :placeholder="t('admin.challenges.searchPlaceholder')" class="pl-10" />
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
                <span>{{ t('admin.challenges.loading') }}</span>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.challenges.empty') }}
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
                <DropdownMenuContent align="end" class="w-[180px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem @click="openEdit(row.original)">
                    <Edit class="mr-2 size-4" />
                    {{ t('common.edit') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem @click="openReveal(row.original)">
                    <Key class="mr-2 size-4" />
                    {{ t('admin.challenges.revealSecret') }}
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
        <Button variant="outline" size="sm" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">
          {{ t('common.previous') }}
        </Button>
        <Button variant="outline" size="sm" :disabled="!table.getCanNextPage()" @click="table.nextPage()">
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

    <!-- Create/Edit Dialog -->
    <Dialog v-model:open="editDialog">
      <DialogContent class="sm:max-w-[600px] max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? t('admin.challenges.createDialogTitle') : t('admin.challenges.editDialogTitle') }}</DialogTitle>
          <DialogDescription>Define challenge settings and deployment details.</DialogDescription>
        </DialogHeader>
        <div class="grid gap-6 py-4">
          <div class="grid grid-cols-2 gap-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.competition') }}</Label>
              <Select v-model="form.competitionId">
                <SelectTrigger>
                  <SelectValue :placeholder="t('admin.challenges.selectCompetition')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="comp in competitions" :key="comp.id" :value="comp.id">{{ comp.title }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.type') }}</Label>
              <Select v-model="form.typeId">
                <SelectTrigger>
                  <SelectValue placeholder="Select type" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="ctf">CTF</SelectItem>
                  <SelectItem value="awd">AWD</SelectItem>
                  <SelectItem value="awdp">AWDP</SelectItem>
                  <SelectItem value="koh">KoH</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div class="grid gap-2">
            <Label for="ctitle">{{ t('admin.challenges.titleColumn') }}</Label>
            <Input id="ctitle" v-model="form.title" />
          </div>

          <div class="grid gap-2">
            <Label for="cdesc">{{ t('admin.challenges.description') }}</Label>
            <Textarea id="cdesc" v-model="form.description" class="resize-none" rows="2" />
          </div>

          <div class="grid gap-2">
            <Label class="flex items-center gap-2">
              <Key class="size-3.5" />
              {{ t('admin.challenges.flagSecret') }}
            </Label>
            <Input v-model="form.flagSecret" placeholder="Static flag or secret prefix" />
          </div>

          <div class="border rounded-lg p-4 space-y-4 bg-muted/30">
            <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-wider">
              <Box class="size-4" />
              Deployment & Container
            </div>
            
            <div class="grid grid-cols-2 gap-4">
              <div class="grid gap-2">
                <Label>{{ t('admin.challenges.containerMode') }}</Label>
                <Select v-model="form.containerMode">
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="SingleImage">{{ t('admin.challenges.singleImage') }}</SelectItem>
                    <SelectItem value="DockerCompose">{{ t('admin.challenges.dockerCompose') }}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div class="grid gap-2" v-if="form.containerMode === 'SingleImage'">
                <Label>{{ t('admin.challenges.containerImage') }}</Label>
                <Input v-model="form.containerImage" placeholder="e.g. noctf/web-ch:latest" />
              </div>
              <div class="grid gap-2" v-else>
                <Label>Project Name</Label>
                <Input v-model="form.composeProjectName" placeholder="compose-proj" />
              </div>
            </div>

            <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
              <Label>Compose YAML</Label>
              <Textarea v-model="form.composeYaml" class="font-mono text-xs" rows="6" />
            </div>

            <div class="grid grid-cols-2 gap-4">
              <div class="grid gap-2">
                <Label>Checker Image</Label>
                <Input v-model="form.checkerImage" placeholder="Optional" />
              </div>
              <div class="grid gap-2">
                <Label>Checker Command</Label>
                <Input v-model="form.checkerCommand" placeholder="Optional" />
              </div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-4">
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.initialPoints') }}</Label>
              <Input v-model.number="form.initialPoints" type="number" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.minPoints') }}</Label>
              <Input v-model.number="form.minimumPoints" type="number" />
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
          <DialogTitle>{{ t('admin.challenges.deleteDialogTitle') }}</DialogTitle>
          <DialogDescription>Permanently remove this challenge.</DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <p class="text-sm">Delete <span class="font-bold">"{{ selectedChallenge?.title }}"</span>?</p>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">{{ t('common.cancel') }}</Button>
          <Button variant="destructive" :disabled="deleteMutation.isPending.value" @click="deleteMutation.mutate(selectedChallenge!.id)">
            <Loader2 v-if="deleteMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('common.delete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Reveal Secret -->
    <Dialog v-model:open="revealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.revealSecret') }}</DialogTitle>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <p class="text-sm text-muted-foreground">{{ t('admin.challenges.revealSecretAuditHint') }}</p>
          <div v-if="revealedSecret" class="relative group">
            <pre class="bg-muted p-4 rounded-lg font-mono text-sm break-all whitespace-pre-wrap border">{{ revealedSecret }}</pre>
            <Button variant="ghost" size="sm" class="absolute top-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity" @click="revealedSecret = ''">Clear</Button>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="revealDialog = false">{{ t('common.close') }}</Button>
          <Button variant="destructive" :disabled="revealMutation.isPending.value" @click="revealMutation.mutate(selectedChallenge!.id)">
            <Loader2 v-if="revealMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ revealedSecret ? t('common.refresh') : 'Reveal Secret' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
