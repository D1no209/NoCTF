<script setup lang="ts">
import { ref, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import {
  useVueTable,
  getCoreRowModel,
  createColumnHelper,
} from '@tanstack/vue-table'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
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
  Trash2,
  RefreshCw,
  Loader2,
  Activity,
  Cpu,
  Layers,
  Monitor
} from 'lucide-vue-next'
import { Card, CardContent } from '@/components/ui/card'
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

const { data: containers, isLoading, isFetching, refetch } = useQuery({
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
  }
})

function openDestroy(c: ContainerDto) {
  selectedContainer.value = c
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

const columns = [
  columnHelper.accessor('containerId', { 
    header: t('admin.containers.containerId'),
    cell: info => h('code', { class: 'text-[10px] font-mono font-bold bg-muted px-1.5 py-0.5 rounded' }, info.getValue().slice(0, 12))
  }),
  columnHelper.accessor('competitionId',
    { header: t('admin.containers.compId'), cell: info => h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)) }),
  columnHelper.accessor('teamId',
    { header: t('admin.containers.teamId'), cell: info => h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)) }),
  columnHelper.accessor('challengeId',
    { header: t('admin.containers.challengeId'), cell: info => h('span', { class: 'text-[10px] text-muted-foreground' }, info.getValue().slice(0, 8)) }),
  columnHelper.accessor('status', { 
    header: t('admin.containers.status'),
    cell: info => h(Badge, { variant: statusVariant(info.getValue()), class: 'uppercase text-[9px] font-black tracking-widest' }, () => info.getValue())
  }),
]

const table = useVueTable({
  get data() { return containers.value ?? [] },
  columns,
  getCoreRowModel: getCoreRowModel(),
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.containers.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.containers.subtitle') }}</p>
      </div>
      <Button variant="outline" size="sm" @click="refetch()" :disabled="isFetching">
        <RefreshCw class="mr-2 size-4" :class="{ 'animate-spin': isFetching }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <!-- Quick Stats -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <Card class="p-0">
        <CardContent class="flex items-center gap-4 p-3">
          <div class="bg-primary/10 p-2.5 rounded-lg">
            <Layers class="size-5 text-primary" />
          </div>
          <div>
            <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">{{ t('admin.containers.stats.totalInstances') }}</p>
            <p class="text-xl font-bold leading-none">{{ containers?.length || 0 }}</p>
          </div>
        </CardContent>
      </Card>
      <Card class="p-0">
        <CardContent class="flex items-center gap-4 p-3">
          <div class="bg-emerald-500/10 p-2.5 rounded-lg">
            <Activity class="size-5 text-emerald-500" />
          </div>
          <div>
            <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">{{ t('admin.containers.stats.healthy') }}</p>
            <p class="text-xl font-bold leading-none">{{ containers?.filter(c => c.status.toLowerCase() === 'running').length || 0 }}</p>
          </div>
        </CardContent>
      </Card>
      <Card class="p-0">
        <CardContent class="flex items-center gap-4 p-3">
          <div class="bg-rose-500/10 p-2.5 rounded-lg">
            <Cpu class="size-5 text-rose-500" />
          </div>
          <div>
            <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">{{ t('admin.containers.stats.terminated') }}</p>
            <p class="text-xl font-bold leading-none">{{ containers?.filter(c => c.status.toLowerCase() !== 'running').length || 0 }}</p>
          </div>
        </CardContent>
      </Card>
      <Card class="p-0">
        <CardContent class="flex items-center gap-4 p-3">
          <div class="bg-muted p-2.5 rounded-lg">
            <Monitor class="size-5 text-muted-foreground" />
          </div>
          <div>
            <p class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">{{ t('admin.containers.stats.engine') }}</p>
            <p class="text-xl font-bold leading-none uppercase">{{ t('admin.containers.stats.engineValue') }}</p>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card class="p-0 overflow-hidden">
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
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground text-sm">
              {{ t('admin.containers.empty') }}
            </TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id" class="group hover:bg-muted/50 transition-colors">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <component :is="() => cell.renderValue()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <Button 
                variant="ghost" 
                size="icon" 
                class="size-8 text-muted-foreground hover:text-destructive hover:bg-destructive/10 opacity-0 group-hover:opacity-100 transition-opacity"
                @click="openDestroy(row.original)"
              >
                <Trash2 class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

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
        <div class="py-4">
          <div class="p-3 rounded-lg bg-destructive/10 border border-destructive/20 font-mono text-xs text-destructive">
            ID: {{ selectedContainer?.containerId }}
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="destroyDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="destroyMutation.isPending.value"
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
