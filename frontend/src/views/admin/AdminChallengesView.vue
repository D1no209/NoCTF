<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import {
  FlexRender,
  createColumnHelper,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useVueTable,
  type SortingState,
} from '@tanstack/vue-table'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
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
import { Box, Edit, Key, Loader2, MoreHorizontal, Plus, Search, Trash2 } from 'lucide-vue-next'
import { toast } from 'vue-sonner'

interface CheckerConfigDto {
  image?: string
  command?: string
}

interface ChallengeTemplateDto {
  id: string
  title: string
  description?: string
  typeId: string
  containerImage?: string
  containerMode: 'SingleImage' | 'DockerCompose' | number
  composeYaml?: string
  composeProjectName?: string
  attachmentUrl?: string
  checkerConfig?: CheckerConfigDto
}

const qc = useQueryClient()
const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const revealDialog = ref(false)
const selectedTemplate = ref<ChallengeTemplateDto | null>(null)
const isCreating = ref(false)
const revealedSecret = ref('')

const defaultForm = () => ({
  title: '',
  description: '',
  typeId: 'ctf',
  attachmentUrl: '',
  flagSecret: '',
  containerImage: '',
  containerMode: 'SingleImage',
  composeYaml: '',
  composeProjectName: '',
  checkerImage: '',
  checkerCommand: '',
})

const form = ref(defaultForm())
const canSave = computed(() => Boolean(form.value.title.trim()))

const { data: templates, isLoading } = useQuery({
  queryKey: queryKeys.adminChallenges,
  queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
})

function templatePayload() {
  return {
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    typeId: form.value.typeId,
    attachmentUrl: form.value.attachmentUrl.trim() || undefined,
    flagSecret: form.value.flagSecret.trim() || undefined,
    containerImage: form.value.containerImage.trim() || undefined,
    containerMode: form.value.containerMode === 'DockerCompose' ? 1 : 0,
    composeYaml: form.value.composeYaml.trim() || undefined,
    composeProjectName: form.value.composeProjectName.trim() || undefined,
    checkerConfig: (form.value.checkerImage.trim() || form.value.checkerCommand.trim())
      ? { image: form.value.checkerImage.trim() || undefined, command: form.value.checkerCommand.trim() || undefined }
      : undefined,
  }
}

const saveMutation = useMutation({
  mutationFn: async () => {
    if (!canSave.value) throw new Error('invalid_template')
    if (isCreating.value) {
      await adminApi.createChallenge(templatePayload())
    } else {
      await adminApi.updateChallenge(selectedTemplate.value!.id, templatePayload())
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    editDialog.value = false
    toast.success(isCreating.value ? 'Challenge template created.' : 'Challenge template updated.')
  },
  onError: () => toast.error('Failed to save challenge template.'),
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => adminApi.deleteChallenge(id),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    deleteDialog.value = false
    toast.success('Challenge template deleted.')
  },
  onError: () => toast.error('This challenge template is in use or could not be deleted.'),
})

const revealMutation = useMutation({
  mutationFn: async (id: string) => adminApi.revealChallengeSecret<{ flagSecret?: string }>(id),
  onSuccess: (data) => { revealedSecret.value = data.flagSecret ?? '' },
  onError: () => toast.error('Failed to reveal secret.'),
})

function openCreate() {
  isCreating.value = true
  selectedTemplate.value = null
  form.value = defaultForm()
  editDialog.value = true
}

function openEdit(template: ChallengeTemplateDto) {
  isCreating.value = false
  selectedTemplate.value = template
  form.value = {
    title: template.title,
    description: template.description ?? '',
    typeId: template.typeId,
    attachmentUrl: template.attachmentUrl ?? '',
    flagSecret: '',
    containerImage: template.containerImage ?? '',
    containerMode: template.containerMode === 1 || template.containerMode === 'DockerCompose' ? 'DockerCompose' : 'SingleImage',
    composeYaml: template.composeYaml ?? '',
    composeProjectName: template.composeProjectName ?? '',
    checkerImage: template.checkerConfig?.image ?? '',
    checkerCommand: template.checkerConfig?.command ?? '',
  }
  editDialog.value = true
}

function openDelete(template: ChallengeTemplateDto) {
  selectedTemplate.value = template
  deleteDialog.value = true
}

function openReveal(template: ChallengeTemplateDto) {
  selectedTemplate.value = template
  revealedSecret.value = ''
  revealDialog.value = true
}

const columnHelper = createColumnHelper<ChallengeTemplateDto>()
const columns = [
  columnHelper.accessor('title', { header: 'Title', enableSorting: true }),
  columnHelper.accessor('typeId', {
    header: 'Type',
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'uppercase font-bold text-[10px]' }, () => info.getValue()),
  }),
  columnHelper.accessor('attachmentUrl', {
    header: 'Attachment',
    cell: (info) => info.getValue() ? h('span', { class: 'text-xs text-foreground' }, 'Attached') : h('span', { class: 'text-muted-foreground' }, '-'),
  }),
  columnHelper.accessor('containerMode', {
    header: 'Container',
    cell: (info) => h(Badge, { variant: 'outline' }, () => (info.getValue() === 1 || info.getValue() === 'DockerCompose') ? 'Compose' : 'Single'),
  }),
  columnHelper.accessor('containerImage', {
    header: 'Image',
    cell: (info) => info.getValue() ? h('code', { class: 'text-xs bg-muted px-1 rounded' }, info.getValue()) : h('span', { class: 'text-muted-foreground' }, '-'),
  }),
]

const table = useVueTable({
  get data() { return templates.value ?? [] },
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
    <div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">Challenges</h2>
        <p class="text-sm text-muted-foreground">Reusable challenge templates. Scoring and hints are configured per competition.</p>
      </div>
      <Button @click="openCreate">
        <Plus class="mr-2 size-4" />
        Create
      </Button>
    </div>

    <div class="relative w-full max-w-sm">
      <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="globalFilter" placeholder="Search challenge templates..." class="pl-10" />
    </div>

    <div class="overflow-hidden rounded-xl border bg-card shadow-sm">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              class="h-11 px-4"
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
            <TableHead class="w-[80px] text-right px-4">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              <Loader2 class="mr-2 inline size-4 animate-spin" />
              Loading templates...
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              No challenge templates yet.
            </TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id" class="hover:bg-muted/50">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" class="size-8">
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[180px]">
                  <DropdownMenuLabel>Actions</DropdownMenuLabel>
                  <DropdownMenuItem @click="openEdit(row.original)">
                    <Edit class="mr-2 size-4" />
                    Edit
                  </DropdownMenuItem>
                  <DropdownMenuItem @click="openReveal(row.original)">
                    <Key class="mr-2 size-4" />
                    Reveal secret
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem class="text-destructive focus:text-destructive" @click="openDelete(row.original)">
                    <Trash2 class="mr-2 size-4" />
                    Delete
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <Dialog v-model:open="editDialog">
      <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-[680px]">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? 'Create challenge template' : 'Edit challenge template' }}</DialogTitle>
          <DialogDescription>Define reusable challenge information and runtime configuration.</DialogDescription>
        </DialogHeader>

        <div class="grid gap-5 py-4">
          <div class="grid gap-2">
            <Label>Title</Label>
            <Input v-model="form.title" />
          </div>
          <div class="grid gap-2">
            <Label>Description</Label>
            <Textarea v-model="form.description" rows="4" />
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>Type</Label>
              <Select v-model="form.typeId">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="ctf">CTF</SelectItem>
                  <SelectItem value="awd">AWD</SelectItem>
                  <SelectItem value="awdp">AWDP</SelectItem>
                  <SelectItem value="koh">KoH</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>Attachment URL</Label>
              <Input v-model="form.attachmentUrl" placeholder="/api/files/challenge.zip" />
            </div>
          </div>

          <div class="grid gap-2">
            <Label>Flag or secret</Label>
            <Input v-model="form.flagSecret" placeholder="Set only when updating the secret" />
          </div>

          <div class="space-y-4 rounded-lg border bg-muted/30 p-4">
            <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-wider">
              <Box class="size-4" />
              Runtime containers
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="grid gap-2">
                <Label>Challenge container mode</Label>
                <Select v-model="form.containerMode">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="SingleImage">Single image</SelectItem>
                    <SelectItem value="DockerCompose">Docker Compose</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div v-if="form.containerMode === 'SingleImage'" class="grid gap-2">
                <Label>Challenge image</Label>
                <Input v-model="form.containerImage" placeholder="noctf/pwn-baby:latest" />
              </div>
              <div v-else class="grid gap-2">
                <Label>Compose project name</Label>
                <Input v-model="form.composeProjectName" />
              </div>
            </div>
            <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
              <Label>Compose YAML</Label>
              <Textarea v-model="form.composeYaml" class="font-mono text-xs" rows="7" />
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="grid gap-2">
                <Label>Check container image</Label>
                <Input v-model="form.checkerImage" placeholder="Optional" />
              </div>
              <div class="grid gap-2">
                <Label>Check command</Label>
                <Input v-model="form.checkerCommand" placeholder="Optional" />
              </div>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" :disabled="saveMutation.isPending.value" @click="editDialog = false">Cancel</Button>
          <Button :disabled="saveMutation.isPending.value || !canSave" @click="saveMutation.mutate()">
            <Loader2 v-if="saveMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ isCreating ? 'Create' : 'Save' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="deleteDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete challenge template</DialogTitle>
          <DialogDescription>Templates already used by competitions cannot be deleted.</DialogDescription>
        </DialogHeader>
        <p class="text-sm">Delete <span class="font-bold">{{ selectedTemplate?.title }}</span>?</p>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">Cancel</Button>
          <Button variant="destructive" :disabled="deleteMutation.isPending.value" @click="deleteMutation.mutate(selectedTemplate!.id)">
            Delete
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="revealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reveal secret</DialogTitle>
          <DialogDescription>This action is written to the audit log.</DialogDescription>
        </DialogHeader>
        <pre v-if="revealedSecret" class="rounded-lg border bg-muted p-4 text-sm whitespace-pre-wrap break-all">{{ revealedSecret }}</pre>
        <DialogFooter>
          <Button variant="outline" @click="revealDialog = false">Close</Button>
          <Button variant="destructive" :disabled="revealMutation.isPending.value" @click="revealMutation.mutate(selectedTemplate!.id)">
            <Loader2 v-if="revealMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            Reveal
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
