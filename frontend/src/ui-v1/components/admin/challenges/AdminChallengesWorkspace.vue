<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
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
import ChallengeTemplateForm from '@/ui-v1/components/admin/ChallengeTemplateForm.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Input } from '@/ui-v1/components/ui/input'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/ui-v1/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/ui-v1/components/ui/dropdown-menu'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'
import { Edit, Key, Loader2, MoreHorizontal, Plus, Search, Trash2 } from 'lucide-vue-next'
import { Card } from '@/ui-v1/components/ui/card'
import {
  challengeTypeOptions,
  deploymentTypeKey,
  deploymentTypeKeys,
  normalizeChallengeType,
  type ChallengeTemplateDto,
  type ChallengeTemplateSubmit,
} from '@/features/admin/challengeTemplate'
import { useAdminChallengesPage } from '@/features/admin/useAdminChallengesPage'

const { t } = useI18n()
const globalFilter = ref('')
const challengeTypeFilter = ref('all')
const attachmentFilter = ref('all')
const deploymentTypeFilter = ref('all')
const sorting = ref<SortingState>([])
const editDialog = ref(false)
const deleteDialog = ref(false)
const revealDialog = ref(false)

const {
  templates,
  isLoading,
  selectedTemplate,
  revealedSecret,
  saveMutation: save,
  deleteMutation: removeChallenge,
  revealMutation: reveal,
  goCreateChallenge,
} = useAdminChallengesPage()

const saveMutation = useToastMutation<ChallengeTemplateSubmit>(save, {
  success: 'admin.challenges.updateSuccess',
  error: 'admin.challenges.saveError',
}, {
  onSuccess: () => {
    editDialog.value = false
  },
})

const deleteMutation = useToastMutation<string>(removeChallenge, {
  success: 'admin.challenges.deleteSuccess',
  error: 'admin.challenges.deleteError',
}, {
  onSuccess: () => {
    deleteDialog.value = false
  },
})

const revealMutation = useToastMutation<string>(reveal, {
  error: 'admin.challenges.revealError',
})

function challengeTypeLabel(value?: string | null) {
  const normalized = normalizeChallengeType(value)
  return challengeTypeOptions.find(option => option.value === normalized)?.label ?? 'CTF'
}

function openCreate() {
  goCreateChallenge()
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
    filterFn: (row, columnId, filterValue) => normalizeChallengeType(row.getValue<string>(columnId)) === filterValue,
    cell: (info) => h(Badge, { variant: 'secondary', class: 'font-bold text-[10px]' }, () => challengeTypeLabel(info.getValue())),
  }),
  columnHelper.accessor('attachmentUrl', {
    header: () => t('admin.challenges.attachment'),
    filterFn: (row, columnId, filterValue) => filterValue === 'attached'
      ? Boolean(row.getValue(columnId))
      : !row.getValue(columnId),
    cell: (info) => info.getValue() ? h('span', { class: 'text-xs text-foreground' }, t('admin.challenges.attached')) : h('span', { class: 'text-muted-foreground' }, '-'),
  }),
  columnHelper.accessor('deploymentType', {
    header: () => t('admin.challenges.deploymentType'),
    filterFn: (row, columnId, filterValue) => deploymentTypeKey(row.getValue<ChallengeTemplateDto['deploymentType']>(columnId)) === filterValue,
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

watch(globalFilter, () => table.setPageIndex(0))
watch(challengeTypeFilter, (value) => {
  table.getColumn('typeId')?.setFilterValue(value === 'all' ? undefined : value)
  table.setPageIndex(0)
})
watch(attachmentFilter, (value) => {
  table.getColumn('attachmentUrl')?.setFilterValue(value === 'all' ? undefined : value)
  table.setPageIndex(0)
})
watch(deploymentTypeFilter, (value) => {
  table.getColumn('deploymentType')?.setFilterValue(value === 'all' ? undefined : value)
  table.setPageIndex(0)
})

const filteredTemplateCount = computed(() => table.getFilteredRowModel().rows.length)
const pageCount = computed(() => Math.max(1, table.getPageCount()))
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

    <Card class="grid gap-3 p-3 md:grid-cols-2 xl:grid-cols-[minmax(16rem,1fr)_repeat(3,minmax(8rem,0.55fr))]">
      <div class="relative w-full">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="globalFilter" :placeholder="t('admin.challenges.searchPlaceholder')" class="pl-10" />
      </div>
      <Select v-model="challengeTypeFilter">
        <SelectTrigger :aria-label="t('admin.challenges.filterChallengeType')">
          <SelectValue :placeholder="t('admin.challenges.allChallengeTypes')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">
            {{ t('admin.challenges.allChallengeTypes') }}
          </SelectItem>
          <SelectItem v-for="option in challengeTypeOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="attachmentFilter">
        <SelectTrigger :aria-label="t('admin.challenges.filterAttachment')">
          <SelectValue :placeholder="t('admin.challenges.allAttachmentStates')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">
            {{ t('admin.challenges.allAttachmentStates') }}
          </SelectItem>
          <SelectItem value="attached">
            {{ t('admin.challenges.withAttachment') }}
          </SelectItem>
          <SelectItem value="none">
            {{ t('admin.challenges.withoutAttachment') }}
          </SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="deploymentTypeFilter">
        <SelectTrigger :aria-label="t('admin.challenges.filterDeploymentType')">
          <SelectValue :placeholder="t('admin.challenges.allDeploymentTypes')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">
            {{ t('admin.challenges.allDeploymentTypes') }}
          </SelectItem>
          <SelectItem v-for="key in deploymentTypeKeys" :key="key" :value="key">
            {{ t(`admin.challenges.deploymentTypes.${key}`) }}
          </SelectItem>
        </SelectContent>
      </Select>
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

    <div class="flex flex-col gap-3 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
      <p class="text-xs text-muted-foreground">
        {{ t('common.pageOf', { page: table.getState().pagination.pageIndex + 1, total: pageCount }) }}
        · {{ t('admin.challenges.filteredCount', { filtered: filteredTemplateCount, total: templates?.length ?? 0 }) }}
      </p>
      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" :disabled="!table.getCanPreviousPage()" @click="table.previousPage()">
          {{ t('common.previous') }}
        </Button>
        <Button variant="outline" size="sm" :disabled="!table.getCanNextPage()" @click="table.nextPage()">
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

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
