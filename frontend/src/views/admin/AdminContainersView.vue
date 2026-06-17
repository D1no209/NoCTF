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

const { data: containers, isLoading } = useQuery({
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
  },
})

function openDestroy(c: ContainerDto) {
  selectedContainer.value = c
  destroyDialog.value = true
}

const columnHelper = createColumnHelper<ContainerDto>()

const columns = [
  columnHelper.accessor('containerId', { header: t('admin.containers.containerId') }),
  columnHelper.accessor('competitionId', { header: t('admin.containers.competitionId') }),
  columnHelper.accessor('teamId', { header: t('admin.containers.teamId') }),
  columnHelper.accessor('challengeId', { header: t('admin.containers.challengeId') }),
  columnHelper.accessor('status', { header: t('admin.containers.status') }),
  columnHelper.display({
    id: 'actions',
    header: t('common.actions'),
    cell: (info) => {
      const c = info.row.original
      return h(Button, { size: 'sm', variant: 'destructive', onClick: () => openDestroy(c) }, () => t('admin.containers.destroy'))
    },
  }),
]

const table = useVueTable({
  get data() { return containers.value ?? [] },
  columns,
  getCoreRowModel: getCoreRowModel(),
})
</script>

<template>
  <div class="space-y-4 p-4 md:p-6">
    <PageHeader :title="t('admin.containers.title')" />

    <ResponsiveTableShell dense min-width="980px">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead v-for="header in headerGroup.headers" :key="header.id">
              <template v-if="!header.isPlaceholder">
                {{ header.column.columnDef.header as string }}
              </template>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.containers.loading') }}</TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.containers.empty') }}</TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
              <component :is="() => cell.renderValue()" v-if="cell.column.id === 'actions'" />
              <template v-else>{{ cell.getValue() }}</template>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </ResponsiveTableShell>

    <!-- Destroy Confirmation -->
    <Dialog v-model:open="destroyDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('admin.containers.destroyDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm py-2" v-html="t('admin.containers.destroyConfirm', { id: selectedContainer?.containerId })"></p>
        <DialogFooter>
          <Button variant="outline" @click="destroyDialog = false">{{ t('common.cancel') }}</Button>
          <Button
            variant="destructive"
            :disabled="destroyMutation.isPending.value"
            @click="destroyMutation.mutate(selectedContainer!.containerId)"
          >
            {{ t('admin.containers.destroy') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
