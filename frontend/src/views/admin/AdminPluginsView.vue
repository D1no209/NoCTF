<script setup lang="ts">
import { h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import {
  useVueTable,
  getCoreRowModel,
  createColumnHelper,
} from '@tanstack/vue-table'
import { client } from '@/api/generated/client.gen'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

const { t } = useI18n()

interface PluginDto {
  name: string
  type: string
  version: string
}

const { data: plugins, isLoading } = useQuery({
  queryKey: ['admin-plugins'],
  queryFn: async () => {
    const res = await client.get<{ 200: PluginDto[] }, unknown, false>({
      url: '/api/admin/plugins',
    })
    return res.data ?? []
  },
})

function typeVariant(type: string): 'default' | 'secondary' | 'outline' | 'destructive' {
  if (type === 'GameMode') return 'default'
  if (type === 'ChallengeType') return 'secondary'
  if (type === 'ContainerProvider') return 'outline'
  return 'outline'
}

const columnHelper = createColumnHelper<PluginDto>()

const columns = [
  columnHelper.accessor('name', { header: t('admin.plugins.name') }),
  columnHelper.accessor('type', {
    header: t('admin.plugins.type'),
    cell: (info) => h(Badge, { variant: typeVariant(info.getValue()) }, () => info.getValue()),
  }),
  columnHelper.accessor('version', { header: t('admin.plugins.version') }),
]

const table = useVueTable({
  get data() { return plugins.value ?? [] },
  columns,
  getCoreRowModel: getCoreRowModel(),
})
</script>

<template>
  <div class="p-6 space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold">{{ t('admin.plugins.title') }}</h1>
    </div>

    <div class="rounded-md border">
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
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.plugins.loading') }}</TableCell>
          </TableRow>
          <TableRow v-else-if="table.getRowModel().rows.length === 0">
            <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.plugins.empty') }}</TableCell>
          </TableRow>
          <TableRow v-else v-for="row in table.getRowModel().rows" :key="row.id">
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
              <component :is="() => cell.renderValue()" v-if="cell.column.id === 'type'" />
              <template v-else>{{ cell.getValue() }}</template>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
