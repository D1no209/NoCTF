<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { useI18n } from 'vue-i18n'
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
import { challengeDirections, normalizeDirection } from '@/lib/challengeDirections'

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
  flagEnvironmentVariable?: string
  deploymentType: 'NoAttachment' | 'StaticAttachment' | 'DynamicContainer' | 'StaticContainer' | number
  exposedPort?: number | null
  checkerConfig?: CheckerConfigDto
}

const qc = useQueryClient()
const { t } = useI18n()
const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const revealDialog = ref(false)
const selectedTemplate = ref<ChallengeTemplateDto | null>(null)
const isCreating = ref(false)
const revealedSecret = ref('')
const attachmentFile = ref<File | null>(null)

const defaultForm = () => ({
  title: '',
  description: '',
  typeId: 'WEB',
  attachmentUrl: '',
  deploymentType: 'NoAttachment',
  exposedPort: undefined as number | undefined,
  flagSecret: '',
  flagEnvironmentVariable: 'NOCTF_FLAG_UUID',
  containerImage: '',
  containerMode: 'SingleImage',
  composeYaml: '',
  composeProjectName: '',
  checkerImage: '',
  checkerCommand: '',
})

const form = ref(defaultForm())
const canSave = computed(() => Boolean(form.value.title.trim()))
const deploymentTypeKeys = ['NoAttachment', 'StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
type DeploymentTypeKey = typeof deploymentTypeKeys[number]

function deploymentTypeKey(value: ChallengeTemplateDto['deploymentType']) {
  return typeof value === 'number' ? deploymentTypeKeys[value] ?? 'NoAttachment' : value
}

function deploymentTypeValue(value: DeploymentTypeKey) {
  return deploymentTypeKeys.indexOf(value)
}

const { data: templates, isLoading } = useQuery({
  queryKey: queryKeys.adminChallenges,
  queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
})

function templatePayload() {
  return {
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    typeId: normalizeDirection(form.value.typeId),
    attachmentUrl: form.value.attachmentUrl.trim() || undefined,
    deploymentType: deploymentTypeValue(form.value.deploymentType as DeploymentTypeKey),
    exposedPort: form.value.exposedPort ? Number(form.value.exposedPort) : undefined,
    flagSecret: form.value.flagSecret.trim() || undefined,
    flagEnvironmentVariable: form.value.flagEnvironmentVariable.trim() || undefined,
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
    let saved: ChallengeTemplateDto
    if (isCreating.value) {
      saved = await adminApi.createChallenge<ChallengeTemplateDto>(templatePayload())
    } else {
      saved = await adminApi.updateChallenge<ChallengeTemplateDto>(selectedTemplate.value!.id, templatePayload())
    }
    if (attachmentFile.value) {
      await adminApi.uploadChallengeAttachment(saved.id, attachmentFile.value)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    editDialog.value = false
    attachmentFile.value = null
    toast.success(isCreating.value ? t('admin.challenges.createSuccess') : t('admin.challenges.updateSuccess'))
  },
  onError: () => toast.error(t('admin.challenges.saveError')),
})

const deleteMutation = useMutation({
  mutationFn: async (id: string) => adminApi.deleteChallenge(id),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    deleteDialog.value = false
    toast.success(t('admin.challenges.deleteSuccess'))
  },
  onError: () => toast.error(t('admin.challenges.deleteError')),
})

const revealMutation = useMutation({
  mutationFn: async (id: string) => adminApi.revealChallengeSecret<{ flagSecret?: string }>(id),
  onSuccess: (data) => { revealedSecret.value = data.flagSecret ?? '' },
  onError: () => toast.error(t('admin.challenges.revealError')),
})

function openCreate() {
  isCreating.value = true
  selectedTemplate.value = null
  form.value = defaultForm()
  attachmentFile.value = null
  editDialog.value = true
}

function openEdit(template: ChallengeTemplateDto) {
  isCreating.value = false
  selectedTemplate.value = template
  form.value = {
    title: template.title,
    description: template.description ?? '',
    typeId: normalizeDirection(template.typeId),
    attachmentUrl: template.attachmentUrl ?? '',
    deploymentType: deploymentTypeKey(template.deploymentType),
    exposedPort: template.exposedPort ?? undefined,
    flagSecret: '',
    flagEnvironmentVariable: template.flagEnvironmentVariable ?? 'NOCTF_FLAG_UUID',
    containerImage: template.containerImage ?? '',
    containerMode: template.containerMode === 1 || template.containerMode === 'DockerCompose' ? 'DockerCompose' : 'SingleImage',
    composeYaml: template.composeYaml ?? '',
    composeProjectName: template.composeProjectName ?? '',
    checkerImage: template.checkerConfig?.image ?? '',
    checkerCommand: template.checkerConfig?.command ?? '',
  }
  attachmentFile.value = null
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
  columnHelper.accessor('title', { header: () => t('admin.challenges.titleColumn'), enableSorting: true }),
  columnHelper.accessor('typeId', {
    header: () => t('admin.challenges.direction'),
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'uppercase font-bold text-[10px]' }, () => info.getValue()),
  }),
  columnHelper.accessor('attachmentUrl', {
    header: () => t('admin.challenges.attachment'),
    cell: (info) => info.getValue() ? h('span', { class: 'text-xs text-foreground' }, t('admin.challenges.attached')) : h('span', { class: 'text-muted-foreground' }, '-'),
  }),
  columnHelper.accessor('deploymentType', {
    header: () => t('admin.challenges.deploymentType'),
    cell: (info) => h(Badge, { variant: 'outline' }, () => t(`admin.challenges.deploymentTypes.${deploymentTypeKey(info.getValue())}`)),
  }),
  columnHelper.accessor('containerMode', {
    header: () => t('admin.challenges.containerMode'),
    cell: (info) => h(Badge, { variant: 'outline' }, () => (info.getValue() === 1 || info.getValue() === 'DockerCompose') ? t('admin.challenges.dockerCompose') : t('admin.challenges.singleImage')),
  }),
  columnHelper.accessor('containerImage', {
    header: () => t('admin.challenges.containerImage'),
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
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.challenges.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.challenges.subtitle') }}</p>
      </div>
      <Button @click="openCreate">
        <Plus class="mr-2 size-4" />
        {{ t('admin.challenges.create') }}
      </Button>
    </div>

    <div class="relative w-full max-w-sm">
      <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="globalFilter" :placeholder="t('admin.challenges.searchPlaceholder')" class="pl-10" />
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
            <TableHead class="w-[80px] text-right px-4">{{ t('common.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              <Loader2 class="mr-2 inline size-4 animate-spin" />
              {{ t('admin.challenges.loading') }}
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground">
              {{ t('admin.challenges.empty') }}
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
    </div>

    <Dialog v-model:open="editDialog">
      <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-[680px]">
        <DialogHeader>
          <DialogTitle>{{ isCreating ? t('admin.challenges.createDialogTitle') : t('admin.challenges.editDialogTitle') }}</DialogTitle>
          <DialogDescription>{{ t('admin.challenges.dialogDescription') }}</DialogDescription>
        </DialogHeader>

        <div class="grid gap-5 py-4">
          <div class="grid gap-2">
            <Label>{{ t('admin.challenges.titleColumn') }}</Label>
            <Input v-model="form.title" />
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.challenges.description') }}</Label>
            <Textarea v-model="form.description" rows="4" />
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.direction') }}</Label>
              <Select v-model="form.typeId">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="direction in challengeDirections" :key="direction" :value="direction">
                    {{ direction }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.attachmentUrl') }}</Label>
              <Input v-model="form.attachmentUrl" placeholder="/api/files/challenge.zip" />
            </div>
            <div class="grid gap-2 sm:col-span-2">
              <Label>{{ t('admin.challenges.attachmentUpload') }}</Label>
              <Input type="file" @change="attachmentFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
            </div>
          </div>

          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.deploymentType') }}</Label>
              <Select v-model="form.deploymentType">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="NoAttachment">{{ t('admin.challenges.deploymentTypes.NoAttachment') }}</SelectItem>
                  <SelectItem value="StaticAttachment">{{ t('admin.challenges.deploymentTypes.StaticAttachment') }}</SelectItem>
                  <SelectItem value="DynamicContainer">{{ t('admin.challenges.deploymentTypes.DynamicContainer') }}</SelectItem>
                  <SelectItem value="StaticContainer">{{ t('admin.challenges.deploymentTypes.StaticContainer') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.exposedPort') }}</Label>
              <Input v-model.number="form.exposedPort" type="number" min="1" max="65535" :placeholder="t('admin.challenges.exposedPortPlaceholder')" />
            </div>
          </div>

          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.flagSecret') }}</Label>
              <Input v-model="form.flagSecret" :placeholder="t('admin.challenges.flagSecretPlaceholder')" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.challenges.flagEnvironmentVariable') }}</Label>
              <Input v-model="form.flagEnvironmentVariable" placeholder="NOCTF_FLAG_UUID" />
            </div>
          </div>

          <div class="space-y-4 rounded-lg border bg-muted/30 p-4">
            <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-wider">
              <Box class="size-4" />
              {{ t('admin.challenges.runtimeContainers') }}
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="grid gap-2">
                <Label>{{ t('admin.challenges.challengeContainerMode') }}</Label>
                <Select v-model="form.containerMode">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="SingleImage">{{ t('admin.challenges.singleImage') }}</SelectItem>
                    <SelectItem value="DockerCompose">{{ t('admin.challenges.dockerCompose') }}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div v-if="form.containerMode === 'SingleImage'" class="grid gap-2">
                <Label>{{ t('admin.challenges.challengeImage') }}</Label>
                <Input v-model="form.containerImage" placeholder="noctf/pwn-baby:latest" />
              </div>
              <div v-else class="grid gap-2">
                <Label>{{ t('admin.challenges.composeProjectName') }}</Label>
                <Input v-model="form.composeProjectName" />
              </div>
            </div>
            <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
              <Label>{{ t('admin.challenges.composeYaml') }}</Label>
              <Textarea v-model="form.composeYaml" class="font-mono text-xs" rows="7" />
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="grid gap-2">
                <Label>{{ t('admin.challenges.checkerImage') }}</Label>
                <Input v-model="form.checkerImage" :placeholder="t('admin.challenges.optional')" />
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.challenges.checkerCommand') }}</Label>
                <Input v-model="form.checkerCommand" :placeholder="t('admin.challenges.optional')" />
              </div>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" :disabled="saveMutation.isPending.value" @click="editDialog = false">{{ t('common.cancel') }}</Button>
          <Button :disabled="saveMutation.isPending.value || !canSave" @click="saveMutation.mutate()">
            <Loader2 v-if="saveMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ isCreating ? t('common.create') : t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="deleteDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.deleteDialogTitle') }}</DialogTitle>
          <DialogDescription>{{ t('admin.challenges.deleteDialogDescription') }}</DialogDescription>
        </DialogHeader>
        <p class="text-sm">{{ t('admin.challenges.deletePrompt') }} <span class="font-bold">{{ selectedTemplate?.title }}</span>?</p>
        <DialogFooter>
          <Button variant="outline" @click="deleteDialog = false">{{ t('common.cancel') }}</Button>
          <Button variant="destructive" :disabled="deleteMutation.isPending.value" @click="deleteMutation.mutate(selectedTemplate!.id)">
            {{ t('common.delete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="revealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.revealSecret') }}</DialogTitle>
          <DialogDescription>{{ t('admin.challenges.revealSecretAuditHint') }}</DialogDescription>
        </DialogHeader>
        <pre v-if="revealedSecret" class="rounded-lg border bg-muted p-4 text-sm whitespace-pre-wrap break-all">{{ revealedSecret }}</pre>
        <DialogFooter>
          <Button variant="outline" @click="revealDialog = false">{{ t('common.close') }}</Button>
          <Button variant="destructive" :disabled="revealMutation.isPending.value" @click="revealMutation.mutate(selectedTemplate!.id)">
            <Loader2 v-if="revealMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('admin.challenges.revealSecret') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
