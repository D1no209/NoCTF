<script setup lang="ts">
import { h, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
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
import ChallengeTemplateForm from '@/components/admin/ChallengeTemplateForm.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
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
import { Edit, Key, Loader2, MoreHorizontal, Plus, Search, Trash2 } from 'lucide-vue-next'
import { Card } from '@/components/ui/card'
import { toast } from 'vue-sonner'

interface CheckerConfigDto {
  image?: string
  command?: string
  timeoutSeconds?: number | null
  expImage?: string | null
  expCommand?: string | null
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
  patchTemplateUrl?: string
  flagEnvironmentVariable?: string
  deploymentType: 'NoAttachment' | 'StaticAttachment' | 'DynamicContainer' | 'StaticContainer' | number
  exposedPort?: number | null
  checkerConfig?: CheckerConfigDto
}

const qc = useQueryClient()
const router = useRouter()
const { t } = useI18n()
const globalFilter = ref('')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const revealDialog = ref(false)
const selectedTemplate = ref<ChallengeTemplateDto | null>(null)
const revealedSecret = ref('')
const deploymentTypeKeys = ['NoAttachment', 'StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
const challengeTypeOptions = [
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
] as const
type ChallengeTypeKey = typeof challengeTypeOptions[number]['value']

function deploymentTypeKey(value: ChallengeTemplateDto['deploymentType']) {
  return typeof value === 'number' ? deploymentTypeKeys[value] ?? 'NoAttachment' : value
}
function normalizeChallengeType(value?: string | null): ChallengeTypeKey {
  const key = (value ?? 'Ctf').trim().toLowerCase()
  if (key === 'awd') return 'Awd'
  if (key === 'awdp') return 'Awdp'
  if (key === 'koh') return 'Koh'
  return 'Ctf'
}

function challengeTypeLabel(value?: string | null) {
  const normalized = normalizeChallengeType(value)
  return challengeTypeOptions.find(option => option.value === normalized)?.label ?? 'CTF'
}

const { data: templates, isLoading } = useQuery({
  queryKey: queryKeys.adminChallenges,
  queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
})

const saveMutation = useMutation({
  mutationFn: async ({ payload, attachmentFile, patchTemplateFile }: { payload: Record<string, unknown>; attachmentFile: File | null; patchTemplateFile: File | null }) => {
    const saved = await adminApi.updateChallenge<ChallengeTemplateDto>(selectedTemplate.value!.id, payload)
    if (attachmentFile) {
      await adminApi.uploadChallengeAttachment(saved.id, attachmentFile)
    }
    if (patchTemplateFile) {
      await adminApi.uploadChallengePatchTemplate(saved.id, patchTemplateFile)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    editDialog.value = false
    toast.success(t('admin.challenges.updateSuccess'))
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
  router.push({ name: 'admin-challenge-create' })
}

function openEdit(template: ChallengeTemplateDto) {
  selectedTemplate.value = template
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
    header: () => t('admin.challenges.challengeMode'),
    enableSorting: true,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'font-bold text-[10px]' }, () => challengeTypeLabel(info.getValue())),
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

    <Card class="grid gap-3 p-3 md:grid-cols-[minmax(0,24rem)]">
      <div class="relative w-full max-w-sm">
      <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="globalFilter" :placeholder="t('admin.challenges.searchPlaceholder')" class="pl-10" />
      </div>
    </Card>

    <Card class="p-0 overflow-hidden">
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
    </Card>

    <Dialog v-model:open="editDialog">
      <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-[680px]">
        <DialogHeader>
          <DialogTitle>{{ t('admin.challenges.editDialogTitle') }}</DialogTitle>
          <DialogDescription>{{ t('admin.challenges.dialogDescription') }}</DialogDescription>
        </DialogHeader>
        <ChallengeTemplateForm
          class="py-4"
          :template="selectedTemplate"
          :saving="saveMutation.isPending.value"
          :submit-text="t('common.save')"
          :cancel-text="t('common.cancel')"
          @submit="saveMutation.mutate($event)"
          @cancel="editDialog = false"
        />
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
