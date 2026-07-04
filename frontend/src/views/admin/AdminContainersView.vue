<script setup lang="ts">
import { computed, ref, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import { useVueTable, getCoreRowModel, createColumnHelper } from '@tanstack/vue-table'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
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
  DialogDescription,
} from '@/components/ui/dialog'
import { Trash2, RefreshCw, Loader2, Activity, Cpu, Layers, Monitor } from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import { vAutoAnimate } from '@formkit/auto-animate/vue'

const { t } = useI18n()
const qc = useQueryClient()

interface ContainerDto {
  containerId: string
  competitionId: string
  teamId: string
  challengeId: string
  status: string
}

const destroyDialog = ref(false)
const selectedContainer = ref<ContainerDto | null>(null)
const destroyConfirmText = ref('')
const destroyConfirmed = computed(
  () => destroyConfirmText.value === (selectedContainer.value?.containerId ?? ''),
)

const {
  data: containers,
  isLoading,
  isFetching,
  refetch,
} = useQuery({
  queryKey: queryKeys.adminContainers,
  queryFn: () => adminApi.containers<ContainerDto[]>(),
})

const destroyMutation = useMutation({
  mutationFn: async (containerId: string) => {
    await adminApi.destroyContainer(containerId)
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminContainers })
    destroyDialog.value = false
    toast.success(t('admin.containers.destroySuccess'))
  },
  onError: () => {
    toast.error(t('admin.containers.destroyError'))
  },
})

function openDestroy(c: ContainerDto) {
  selectedContainer.value = c
  destroyConfirmText.value = ''
  destroyDialog.value = true
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'running' || s === 'active') return 'default'
  if (s === 'created' || s === 'paused') return 'secondary'
  if (s === 'exited' || s === 'dead') return 'destructive'
  return 'outline'
}

const columnHelper = createColumnHelper<ContainerDto>()

function statusLabel(status: string) {
  const key = status.toLowerCase()
  const knownStatuses = ['running', 'active', 'created', 'paused', 'exited', 'dead']
  if (!knownStatuses.includes(key)) return status
  return t(`admin.containers.statusValues.${key}`)
}

const columns = computed(() => [
  columnHelper.accessor('containerId', {
    header: t('admin.containers.containerId'),
    cell: (info) =>
      h(
        'code',
        { class: 'text-[10px] font-mono font-bold bg-muted px-1.5 py-0.5 rounded' },
        info.getValue().slice(0, 12),
      ),
  }),
  columnHelper.accessor('competitionId', {
    header: t('admin.containers.competitionId'),
    cell: (info) =>
      h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)),
  }),
  columnHelper.accessor('teamId', {
    header: t('admin.containers.teamId'),
    cell: (info) =>
      h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)),
  }),
  columnHelper.accessor('challengeId', {
    header: t('admin.containers.challengeId'),
    cell: (info) =>
      h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)),
  }),
  columnHelper.accessor('status', {
    header: t('admin.containers.status'),
    cell: (info) =>
      h(
        Badge,
        {
          variant: statusVariant(info.getValue()),
          class: 'uppercase text-[9px] font-black tracking-widest',
        },
        () => statusLabel(info.getValue()),
      ),
  }),
])

const table = useVueTable({
  get data() {
    return containers.value ?? []
  },
  get columns() {
    return columns.value
  },
  getCoreRowModel: getCoreRowModel(),
})
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.containers.title') }}</h2>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.containers.subtitle') }}
        </p>
      </div>
      <Button variant="outline" size="sm" @click="refetch()" :disabled="isFetching">
        <RefreshCw class="mr-2 size-4" :class="{ 'animate-spin': isFetching }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div class="noctf-status-strip grid-cols-1 sm:grid-cols-2 lg:grid-cols-4">
      <div class="noctf-status-item border-b sm:border-r lg:border-b-0">
        <div class="rounded-md bg-primary/10 p-2 text-primary">
          <Layers class="size-5 text-primary" />
        </div>
        <div class="min-w-0">
          <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            {{ t('admin.containers.totalInstances') }}
          </p>
          <p class="text-xl font-bold leading-none">{{ containers?.length || 0 }}</p>
        </div>
      </div>
      <div class="noctf-status-item border-b lg:border-b-0 lg:border-r">
        <div class="rounded-md bg-emerald-500/10 p-2 text-emerald-600">
          <Activity class="size-5" />
        </div>
        <div class="min-w-0">
          <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            {{ t('admin.containers.healthy') }}
          </p>
          <p class="text-xl font-bold leading-none">
            {{ containers?.filter((c) => c.status.toLowerCase() === 'running').length || 0 }}
          </p>
        </div>
      </div>
      <div class="noctf-status-item border-b sm:border-b-0 sm:border-r">
        <div class="rounded-md bg-destructive/10 p-2 text-destructive">
          <Cpu class="size-5" />
        </div>
        <div class="min-w-0">
          <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            {{ t('admin.containers.terminated') }}
          </p>
          <p class="text-xl font-bold leading-none">
            {{ containers?.filter((c) => c.status.toLowerCase() !== 'running').length || 0 }}
          </p>
        </div>
      </div>
      <div class="noctf-status-item">
        <div class="rounded-md bg-muted p-2">
          <Monitor class="size-5 text-muted-foreground" />
        </div>
        <div class="min-w-0">
          <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            {{ t('admin.containers.engine') }}
          </p>
          <p class="text-xl font-bold leading-none uppercase">Docker</p>
        </div>
      </div>
    </div>

    <div class="noctf-table-shell">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead v-for="header in headerGroup.headers" :key="header.id" class="px-4 py-3">
              <template v-if="!header.isPlaceholder">
                {{ header.column.columnDef.header as string }}
              </template>
            </TableHead>
            <TableHead class="w-[60px] text-right px-4"></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center">
              <Loader2 class="size-5 animate-spin mx-auto text-muted-foreground" />
            </TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell
              :colspan="columns.length + 1"
              class="h-24 text-center text-muted-foreground text-sm"
            >
              {{ t('admin.containers.empty') }}
            </TableCell>
          </TableRow>
          <TableRow
            v-else
            v-for="row in table.getRowModel().rows"
            :key="row.id"
            class="group hover:bg-muted/50 transition-colors"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <component :is="() => cell.renderValue()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <Button
                variant="ghost"
                size="icon"
                class="size-8 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                :aria-label="t('admin.containers.destroyAriaLabel')"
                @click="openDestroy(row.original)"
              >
                <Trash2 class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <!-- Destroy Confirmation -->
    <Dialog v-model:open="destroyDialog">
      <DialogContent class="sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle class="text-destructive flex items-center gap-2">
            <Trash2 class="size-5" />
            {{ t('admin.containers.destroyDialogTitle') }}
          </DialogTitle>
          <DialogDescription>
            {{ t('admin.containers.destroyDialogDescription') }}
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="noctf-danger-panel break-all font-mono text-xs">
            {{ t('admin.containers.destroyDialogContainerId') }}:
            {{ selectedContainer?.containerId }}
          </div>
          <label class="grid gap-2 text-sm">
            <span class="font-medium">{{ t('admin.containers.destroyConfirmLabel') }}</span>
            <Input v-model="destroyConfirmText" class="font-mono text-xs" autocomplete="off" />
          </label>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="destroyDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="destroyMutation.isPending.value || !destroyConfirmed"
            @click="destroyMutation.mutate(selectedContainer!.containerId)"
          >
            <Loader2 v-if="destroyMutation.isPending.value" class="mr-2 size-4 animate-spin" />
            {{ t('admin.containers.destroy') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
