<script setup lang="ts">
import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '@/api/generated/types.gen'
import { Loader2, RotateCcw, Square } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

defineProps<{
  runtimes?: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse[]
  loading: boolean
  operatingId?: string | null
}>()

const emit = defineEmits<{
  reset: [runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse]
  stop: [runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse]
}>()

const { t } = useI18n()
const runtimeStates = ['Queued', 'Provisioning', 'Running', 'Stopping', 'Stopped', 'Failed'] as const
const runtimeProviders = ['Docker', 'Kubernetes', 'Libvirt'] as const

function runtimeState(runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
  return runtime.state === undefined ? 'Unknown' : runtimeStates[runtime.state]
}

function runtimeProvider(runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
  return runtime.provider === undefined ? 'Unknown' : runtimeProviders[runtime.provider]
}
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">{{ t('admin.competitionDetail.runtimeTitle') }}</h3>
      <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.runtimeDescription') }}</p>
    </div>
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{{ t('admin.competitionDetail.runtimeInstance') }}</TableHead>
          <TableHead>{{ t('admin.containers.challengeId') }}</TableHead>
          <TableHead>{{ t('admin.containers.teamId') }}</TableHead>
          <TableHead>{{ t('admin.competitionDetail.runtimeProvider') }}</TableHead>
          <TableHead>{{ t('common.status') }}</TableHead>
          <TableHead>{{ t('admin.competitionDetail.runtimeUrls') }}</TableHead>
          <TableHead class="text-right">{{ t('common.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="loading">
          <TableCell colspan="7" class="h-20 text-center text-muted-foreground"><Loader2 class="mr-2 inline size-4 animate-spin" />{{ t('common.loading') }}</TableCell>
        </TableRow>
        <TableRow v-else-if="!runtimes?.length">
          <TableCell colspan="7" class="h-20 text-center text-muted-foreground">{{ t('admin.competitionDetail.noRuntimes') }}</TableCell>
        </TableRow>
        <TableRow v-for="runtime in runtimes" v-else :key="runtime.id">
          <TableCell><code class="text-xs">{{ runtime.id ?? '-' }}</code><div class="text-xs text-muted-foreground">gen {{ runtime.generation ?? '-' }}</div></TableCell>
          <TableCell><code class="text-xs text-muted-foreground">{{ runtime.competitionChallengeId ?? '-' }}</code></TableCell>
          <TableCell><code class="text-xs text-muted-foreground">{{ runtime.teamId ?? t('admin.competitionDetail.sharedRuntime') }}</code></TableCell>
          <TableCell>{{ runtimeProvider(runtime) }}<div class="text-xs text-muted-foreground">{{ runtime.runnerPool ?? '-' }}</div></TableCell>
          <TableCell><Badge variant="outline">{{ runtimeState(runtime) }}</Badge></TableCell>
          <TableCell class="max-w-72"><a v-for="url in runtime.urls ?? []" :key="url" :href="url" target="_blank" rel="noreferrer" class="block truncate text-xs text-primary hover:underline">{{ url }}</a><span v-if="!runtime.urls?.length">-</span></TableCell>
          <TableCell class="text-right">
            <div class="flex justify-end gap-1">
              <Button size="icon" variant="ghost" class="size-8" :disabled="operatingId === runtime.id" :title="t('common.reset')" @click="emit('reset', runtime)"><RotateCcw class="size-4" /></Button>
              <Button size="icon" variant="ghost" class="size-8 text-destructive" :disabled="operatingId === runtime.id" :title="t('common.stop')" @click="emit('stop', runtime)"><Square class="size-4" /></Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </Card>
</template>
