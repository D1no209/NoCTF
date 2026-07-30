<script setup lang="ts">
import type { SortingState } from '@tanstack/vue-table'
import type { ChallengeTemplate } from '@/api/noctf'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  createColumnHelper,
  FlexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useVueTable,
} from '@tanstack/vue-table'
import {
  ArchiveRestore,
  Check,
  Copy,
  Loader2,
  MoreHorizontal,
  RefreshCw,
  Search,
  Trash2,
} from 'lucide-vue-next'
import { computed, h, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { challengeBankAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
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
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  canDeleteTemplate,
  canRestoreTemplate,
  CHALLENGE_MODE,
  CHALLENGE_VISIBILITY,
  challengeModeLabelKey,
  challengeVisibilityLabelKey,
  isDeletedTemplate,
} from './challengeTemplatePresentation'

type LifecycleAction = 'delete' | 'restore'

const { locale, t } = useI18n()
const queryClient = useQueryClient()

const globalFilter = ref('')
const sorting = ref<SortingState>([])
const includeDeleted = ref(false)
const selectedTemplate = ref<ChallengeTemplate | null>(null)
const lifecycleAction = ref<LifecycleAction | null>(null)
const lifecycleDialog = ref(false)
const copiedId = ref<string | null>(null)

const challengeTemplatesQueryKey = computed(() => [
  ...queryKeys.adminChallenges,
  { includeDeleted: includeDeleted.value },
])

const {
  data: templates,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: challengeTemplatesQueryKey,
  queryFn: () => challengeBankAdminApi.templates(includeDeleted.value),
})

const lifecycleMutation = useMutation({
  mutationFn: async ({ action, id }: { action: LifecycleAction, id: string }) => {
    if (action === 'restore')
      await challengeBankAdminApi.restoreTemplate(id)
    else
      await challengeBankAdminApi.deleteTemplate(id)
  },
  onSuccess: (_, variables) => {
    lifecycleDialog.value = false
    selectedTemplate.value = null
    lifecycleAction.value = null
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    toast.success(
      variables.action === 'restore'
        ? t('admin.challenges.restoreSuccess')
        : t('admin.challenges.deleteSuccess'),
    )
  },
  onError: (_, variables) => {
    toast.error(
      variables.action === 'restore'
        ? t('admin.challenges.restoreError')
        : t('admin.challenges.deleteError'),
    )
  },
})

function displayTitle(template: ChallengeTemplate) {
  return template.title?.trim() || template.id || t('admin.challenges.unknownTemplate')
}

function formatTimestamp(value?: string | null) {
  if (!value)
    return ''

  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value

  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

function referenceCount(template: ChallengeTemplate) {
  return template.activeCompetitionReferenceCount ?? 0
}

async function copyStableId(id?: string) {
  if (!id)
    return

  try {
    await navigator.clipboard.writeText(id)
    copiedId.value = id
    toast.success(t('admin.challenges.idCopied'))
    window.setTimeout(() => {
      if (copiedId.value === id)
        copiedId.value = null
    }, 1500)
  }
  catch {
    toast.error(t('admin.challenges.idCopyError'))
  }
}

function openLifecycleDialog(template: ChallengeTemplate, action: LifecycleAction) {
  if (!template.id)
    return
  if (action === 'delete' && !canDeleteTemplate(template))
    return
  if (action === 'restore' && !canRestoreTemplate(template))
    return

  selectedTemplate.value = template
  lifecycleAction.value = action
  lifecycleDialog.value = true
}

function confirmLifecycleAction() {
  const id = selectedTemplate.value?.id
  const action = lifecycleAction.value
  if (!id || !action)
    return

  lifecycleMutation.mutate({ action, id })
}

function onLifecycleDialogChange(open: boolean) {
  lifecycleDialog.value = open
  if (!open && !lifecycleMutation.isPending.value) {
    selectedTemplate.value = null
    lifecycleAction.value = null
  }
}

function modeVariant(mode: ChallengeTemplate['mode']): 'default' | 'secondary' | 'outline' {
  if (mode === CHALLENGE_MODE.awdp)
    return 'default'
  if (mode === CHALLENGE_MODE.awd)
    return 'secondary'
  return 'outline'
}

const columnHelper = createColumnHelper<ChallengeTemplate>()

const columns = [
  columnHelper.accessor(template => template.title ?? '', {
    id: 'title',
    header: () => t('admin.challenges.titleColumn'),
    enableSorting: true,
    cell: (info) => {
      const template = info.row.original
      return h('div', { class: 'min-w-44 space-y-1' }, [
        h(
          'div',
          {
            class: [
              'font-medium',
              isDeletedTemplate(template) ? 'text-muted-foreground line-through' : '',
            ],
          },
          displayTitle(template),
        ),
        template.description
          ? h(
              'p',
              { class: 'max-w-80 truncate text-xs text-muted-foreground' },
              template.description,
            )
          : null,
      ])
    },
  }),
  columnHelper.accessor(template => template.id ?? '', {
    id: 'id',
    header: () => t('admin.challenges.stableId'),
    enableSorting: true,
    cell: (info) => {
      const id = info.getValue()
      if (!id)
        return h('span', { class: 'text-muted-foreground' }, '—')

      return h('div', { class: 'flex min-w-72 items-center gap-1' }, [
        h('code', { class: 'select-all font-mono text-xs text-muted-foreground' }, id),
        h(
          Button,
          {
            'variant': 'ghost',
            'size': 'icon',
            'class': 'size-7 shrink-0',
            'aria-label': t('admin.challenges.copyStableId', { id }),
            'onClick': (event: MouseEvent) => {
              event.stopPropagation()
              void copyStableId(id)
            },
          },
          () =>
            h(copiedId.value === id ? Check : Copy, {
              class: copiedId.value === id ? 'size-3.5 text-emerald-600' : 'size-3.5',
            }),
        ),
      ])
    },
  }),
  columnHelper.accessor('mode', {
    header: () => t('admin.challenges.challengeMode'),
    cell: info =>
      h(Badge, { variant: modeVariant(info.getValue()) }, () =>
        t(challengeModeLabelKey(info.getValue()))),
  }),
  columnHelper.accessor('visibility', {
    header: () => t('admin.challenges.visibility'),
    cell: info =>
      h(
        Badge,
        {
          variant:
            info.getValue() === CHALLENGE_VISIBILITY.shared ? 'secondary' : 'outline',
        },
        () => t(challengeVisibilityLabelKey(info.getValue())),
      ),
  }),
  columnHelper.accessor(template => template.direction ?? '', {
    id: 'direction',
    header: () => t('admin.challenges.direction'),
    enableSorting: true,
    cell: info =>
      h(
        'span',
        { class: 'font-mono text-xs uppercase tracking-wide' },
        info.getValue() || '—',
      ),
  }),
  columnHelper.accessor('revision', {
    header: () => t('admin.challenges.revision'),
    cell: info =>
      h(
        'code',
        { class: 'font-mono text-xs tabular-nums text-muted-foreground' },
        `v${info.getValue() ?? 0}`,
      ),
  }),
  columnHelper.accessor('activeCompetitionReferenceCount', {
    header: () => t('admin.challenges.activeReferences'),
    cell: info =>
      h(
        'span',
        { class: 'tabular-nums' },
        t('admin.challenges.referenceCount', { count: info.getValue() ?? 0 }),
      ),
  }),
  columnHelper.accessor('deletedAt', {
    header: () => t('admin.challenges.lifecycle'),
    cell: (info) => {
      const deletedAt = info.getValue()
      if (!deletedAt)
        return h(Badge, { variant: 'outline' }, () => t('admin.challenges.statusActive'))

      return h('div', { class: 'space-y-1' }, [
        h(Badge, { variant: 'destructive' }, () => t('admin.challenges.statusDeleted')),
        h(
          'time',
          {
            class: 'block whitespace-nowrap text-[11px] text-muted-foreground',
            datetime: deletedAt,
          },
          formatTimestamp(deletedAt),
        ),
      ])
    },
  }),
]

const table = useVueTable({
  get data() {
    return templates.value ?? []
  },
  columns,
  state: {
    get globalFilter() {
      return globalFilter.value
    },
    get sorting() {
      return sorting.value
    },
  },
  onGlobalFilterChange: (value) => {
    globalFilter.value = value
  },
  onSortingChange: (updater) => {
    sorting.value = typeof updater === 'function' ? updater(sorting.value) : updater
  },
  getCoreRowModel: getCoreRowModel(),
  getFilteredRowModel: getFilteredRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
  getSortedRowModel: getSortedRowModel(),
})

const filteredTemplateCount = computed(() => table.getFilteredRowModel().rows.length)
const pageCount = computed(() => Math.max(1, table.getPageCount()))

watch([globalFilter, includeDeleted], () => table.setPageIndex(0))
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
      <div class="space-y-1">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">
            {{ t('admin.challenges.title') }}
          </h2>
          <Badge variant="secondary" class="font-mono text-[10px] uppercase tracking-widest">
            {{ t('admin.challenges.gitOpsInventory') }}
          </Badge>
        </div>
        <p class="max-w-3xl text-sm text-muted-foreground">
          {{ t('admin.challenges.lifecycleSubtitle') }}
        </p>
      </div>
      <Button
        :variant="includeDeleted ? 'default' : 'outline'"
        class="w-full sm:w-auto"
        :aria-pressed="includeDeleted"
        @click="includeDeleted = !includeDeleted"
      >
        <ArchiveRestore class="size-4" />
        {{
          includeDeleted
            ? t('admin.challenges.hideDeleted')
            : t('admin.challenges.includeDeleted')
        }}
      </Button>
    </div>

    <Card class="grid min-w-0 gap-3 p-3 sm:grid-cols-[minmax(0,24rem)_auto] sm:items-center">
      <div class="relative w-full">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="globalFilter"
          :placeholder="t('admin.challenges.searchInventory')"
          class="pl-10"
        />
      </div>
      <p class="text-xs tabular-nums text-muted-foreground sm:text-right">
        {{
          t('admin.challenges.filteredCount', {
            filtered: filteredTemplateCount,
            total: templates?.length ?? 0,
          })
        }}
      </p>
    </Card>

    <Card class="min-w-0 p-0">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              class="h-11 px-4 text-left align-middle font-medium text-muted-foreground"
            >
              <template v-if="!header.isPlaceholder">
                <button
                  v-if="header.column.getCanSort()"
                  type="button"
                  class="flex items-center gap-2 outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  @click="header.column.getToggleSortingHandler()?.($event)"
                >
                  <FlexRender
                    :render="header.column.columnDef.header"
                    :props="header.getContext()"
                  />
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]">
                    ▼
                  </span>
                </button>
                <FlexRender
                  v-else
                  :render="header.column.columnDef.header"
                  :props="header.getContext()"
                />
              </template>
            </TableHead>
            <TableHead class="w-[80px] px-4 text-right">
              {{ t('common.actions') }}
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate>
          <template v-if="isLoading">
            <TableRow v-for="index in 4" :key="`challenge-skeleton-${index}`">
              <TableCell
                v-for="cellIndex in columns.length + 1"
                :key="cellIndex"
                class="px-4 py-3"
              >
                <Skeleton :class="cellIndex <= 2 ? 'h-4 w-48' : 'h-4 w-20'" />
              </TableCell>
            </TableRow>
          </template>
          <TableRow v-else-if="isError">
            <TableCell :colspan="columns.length + 1" class="h-36 text-center">
              <div class="flex flex-col items-center justify-center gap-3 text-muted-foreground">
                <span>{{ t('admin.challenges.loadError') }}</span>
                <Button variant="outline" size="sm" @click="refetch()">
                  <RefreshCw class="size-4" />
                  {{ t('common.refresh') }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length + 1" class="h-32 text-center">
              <p class="font-medium">
                {{ t('admin.challenges.inventoryEmpty') }}
              </p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ t('admin.challenges.inventoryEmptyDetail') }}
              </p>
            </TableCell>
          </TableRow>
          <TableRow
            v-for="row in table.getRowModel().rows"
            v-else
            :key="row.id"
            class="group transition-colors hover:bg-muted/50"
            :class="isDeletedTemplate(row.original) ? 'bg-muted/20' : ''"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8 p-0"
                    :aria-label="
                      t('admin.challenges.actionsFor', { title: displayTitle(row.original) })
                    "
                  >
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-[240px]">
                  <DropdownMenuLabel>{{ t('common.actions') }}</DropdownMenuLabel>
                  <DropdownMenuItem
                    :disabled="!row.original.id"
                    @click="copyStableId(row.original.id)"
                  >
                    <Copy class="mr-2 size-4" />
                    {{ t('admin.challenges.copyId') }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem
                    v-if="canRestoreTemplate(row.original)"
                    @click="openLifecycleDialog(row.original, 'restore')"
                  >
                    <ArchiveRestore class="mr-2 size-4" />
                    {{ t('admin.challenges.restore') }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    v-else
                    class="text-destructive focus:text-destructive"
                    :disabled="!canDeleteTemplate(row.original)"
                    @click="openLifecycleDialog(row.original, 'delete')"
                  >
                    <Trash2 class="mr-2 size-4" />
                    {{
                      referenceCount(row.original) > 0
                        ? t('admin.challenges.deleteBlocked', {
                          count: referenceCount(row.original),
                        })
                        : t('common.delete')
                    }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <div
      class="flex flex-col gap-3 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between"
    >
      <p>
        {{
          t('common.pageOf', {
            page: table.getState().pagination.pageIndex + 1,
            total: pageCount,
          })
        }}
      </p>
      <div class="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="!table.getCanPreviousPage()"
          @click="table.previousPage()"
        >
          {{ t('common.previous') }}
        </Button>
        <Button
          variant="outline"
          size="sm"
          :disabled="!table.getCanNextPage()"
          @click="table.nextPage()"
        >
          {{ t('common.next') }}
        </Button>
      </div>
    </div>

    <Dialog :open="lifecycleDialog" @update:open="onLifecycleDialogChange">
      <DialogContent class="sm:max-w-[480px]">
        <DialogHeader>
          <DialogTitle>
            {{
              lifecycleAction === 'restore'
                ? t('admin.challenges.restoreDialogTitle')
                : t('admin.challenges.deleteDialogTitle')
            }}
          </DialogTitle>
          <DialogDescription>
            {{
              lifecycleAction === 'restore'
                ? t('admin.challenges.restoreDialogDescription')
                : t('admin.challenges.lifecycleDeleteDescription')
            }}
          </DialogDescription>
        </DialogHeader>
        <div v-if="selectedTemplate" class="space-y-3 border bg-muted/30 p-4">
          <p class="font-medium">
            {{ displayTitle(selectedTemplate) }}
          </p>
          <code class="block break-all font-mono text-xs text-muted-foreground">
            {{ selectedTemplate.id }}
          </code>
          <p class="text-xs text-muted-foreground">
            {{
              lifecycleAction === 'restore'
                ? t('admin.challenges.restoreKeepsId')
                : t('admin.challenges.softDeleteNote')
            }}
          </p>
        </div>
        <DialogFooter>
          <Button
            variant="outline"
            :disabled="lifecycleMutation.isPending.value"
            @click="onLifecycleDialogChange(false)"
          >
            {{ t('common.cancel') }}
          </Button>
          <Button
            :variant="lifecycleAction === 'restore' ? 'default' : 'destructive'"
            :disabled="lifecycleMutation.isPending.value"
            @click="confirmLifecycleAction"
          >
            <Loader2 v-if="lifecycleMutation.isPending.value" class="size-4 animate-spin" />
            {{
              lifecycleAction === 'restore'
                ? t('admin.challenges.restore')
                : t('common.delete')
            }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
