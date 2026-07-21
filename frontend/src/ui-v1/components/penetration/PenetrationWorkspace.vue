<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Flag, Layers3, Loader2, Network, RefreshCw, Server, Square, Terminal, Trash2 } from 'lucide-vue-next'
import PageHeader from '@/ui-v1/components/layout/PageHeader.vue'
import DataState from '@/ui-v1/components/state/DataState.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Panel } from '@/ui-v1/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/ui-v1/components/ui/select'
import { Separator } from '@/ui-v1/components/ui/separator'
import {
  usePenetrationDashboardPage,
  type PenetrationFlagResultDto,
  type PenetrationLifecycleAction,
} from '@/features/competitions/usePenetrationDashboardPage'

const {
  selectedChallengeId,
  flag,
  penetrationChallenges,
  loadingChallenges,
  challengesError,
  refetchChallenges,
  detail,
  loadingDetail,
  detailError,
  detailQueryError: error,
  refetchDetail: refetch,
  lifecycleMutation,
  submitFlagMutation,
  instance,
  entryAddress,
} = usePenetrationDashboardPage()

const lifecycle = useToastMutation<PenetrationLifecycleAction>(lifecycleMutation, {
  success: 'Instance updated',
  error: 'Instance operation failed',
})

const submitFlag = {
  isPending: submitFlagMutation.isPending,
  mutate: () => submitFlagMutation.mutate(undefined, {
    onSuccess: (result) => {
      const flagResult = result as PenetrationFlagResultDto | undefined
      if (flagResult?.correct)
        toast.success(flagResult.alreadySolved ? 'Flag was already accepted' : 'Flag accepted')
      else
        toast.error(flagResult?.result ?? 'Flag rejected')
    },
    onError: () => toast.error('Flag submission failed'),
  }),
}

function statusVariant(status?: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const value = status?.toLowerCase() ?? ''
  if (value === 'running') return 'default'
  if (value === 'failed' || value === 'error') return 'destructive'
  if (value === 'stopped' || value === 'destroyed' || value === 'none') return 'outline'
  return 'secondary'
}
</script>

<template>
  <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
    <PageHeader title="Penetration Range" description="Instances, staged objectives, and submitted flags.">
      <template #actions>
        <Badge :variant="statusVariant(instance?.status)">
          {{ instance?.status ?? 'Unavailable' }}
        </Badge>
      </template>
    </PageHeader>

    <DataState
      v-if="loadingChallenges"
      loading
    />
    <DataState
      v-else-if="challengesError"
      error
      retry-label="Retry"
      @retry="refetchChallenges"
    />
    <DataState
      v-else-if="!penetrationChallenges.length"
      empty
      empty-title="No penetration challenges"
      empty-description="This competition has no published penetration range."
    />

    <template v-else>
      <Card class="p-4">
        <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
          <div class="grid gap-2">
            <label class="text-sm font-medium">Challenge</label>
            <Select v-model="selectedChallengeId">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="challenge in penetrationChallenges" :key="challenge.id" :value="challenge.id">
                  {{ challenge.title }} · {{ challenge.points ?? 0 }} pts
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <Button variant="outline" :disabled="loadingDetail" @click="refetch()">
            <RefreshCw class="size-4" />
            Refresh
          </Button>
        </div>
      </Card>

      <DataState
        v-if="loadingDetail"
        loading
      />
      <DataState
        v-else-if="detailError"
        error
        :error-message="error instanceof Error ? error.message : undefined"
        retry-label="Retry"
        @retry="refetch"
      />

      <template v-else-if="detail">
        <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div class="space-y-6">
            <Card>
              <CardHeader>
                <div class="flex items-start justify-between gap-4">
                  <div>
                    <CardTitle class="flex items-center gap-2">
                      <Network class="size-5 text-primary" />
                      {{ detail.topology?.name ?? 'Range topology' }}
                    </CardTitle>
                    <p class="mt-1 text-sm text-muted-foreground">{{ detail.topology?.description }}</p>
                  </div>
                  <Badge variant="outline">{{ detail.solvedStageCount }} / {{ detail.totalStageCount }} stages</Badge>
                </div>
              </CardHeader>
              <CardContent>
                <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                  <Panel v-for="node in detail.topology?.nodes ?? []" :key="node.id" class="min-h-32 space-y-3 p-4">
                    <div class="flex items-start justify-between gap-3">
                      <Server class="size-5 text-primary" />
                      <Badge v-if="node.isEntry" variant="default">Entry</Badge>
                      <Badge v-else variant="outline">{{ node.isInternal ? 'Internal' : 'Public' }}</Badge>
                    </div>
                    <div>
                      <div class="font-semibold">{{ node.name }}</div>
                      <div class="mt-1 text-xs text-muted-foreground">{{ node.role || node.image }}</div>
                    </div>
                  </Panel>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle class="flex items-center gap-2">
                  <Flag class="size-5 text-primary" />
                  Objectives
                </CardTitle>
              </CardHeader>
              <CardContent class="space-y-3">
                <Panel
                  v-for="objective in detail.topology?.flags ?? []"
                  :key="objective.id"
                  class="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div>
                    <div class="flex items-center gap-2">
                      <Badge :variant="objective.solved ? 'default' : 'outline'">Stage {{ objective.stage }}</Badge>
                      <span class="font-medium">{{ objective.name }}</span>
                    </div>
                    <p class="mt-1 text-sm text-muted-foreground">
                      {{ objective.nodeName ?? 'Unassigned node' }} · {{ objective.score }} pts
                    </p>
                    <p v-if="objective.solved && objective.hintAfterSolved" class="mt-2 text-sm text-primary">{{ objective.hintAfterSolved }}</p>
                  </div>
                  <Badge :variant="objective.solved ? 'default' : 'secondary'">{{ objective.solved ? 'Solved' : 'Open' }}</Badge>
                </Panel>
              </CardContent>
            </Card>
          </div>

          <aside class="space-y-6">
            <Card>
              <CardHeader>
                <CardTitle class="flex items-center gap-2">
                  <Terminal class="size-5 text-primary" />
                  Instance
                </CardTitle>
              </CardHeader>
              <CardContent class="space-y-4">
                <Panel class="space-y-2 p-3">
                  <div class="text-xs font-medium uppercase text-muted-foreground">Entry</div>
                  <code class="block break-all text-sm">{{ entryAddress || 'Start an instance to receive an entry point.' }}</code>
                </Panel>
                <div v-if="instance?.lastError" class="border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
                  {{ instance.lastError }}
                </div>
                <div class="grid grid-cols-2 gap-2 text-sm">
                  <Panel class="p-3"><div class="text-xs text-muted-foreground">Resets</div><div class="mt-1 font-mono">{{ instance?.resetCount ?? 0 }} / {{ instance?.resetLimit ?? 0 }}</div></Panel>
                  <Panel class="p-3"><div class="text-xs text-muted-foreground">Score</div><div class="mt-1 font-mono">{{ detail.totalScore }}</div></Panel>
                </div>
                <Separator />
                <div class="grid grid-cols-2 gap-2">
                  <Button :disabled="lifecycle.isPending.value || instance?.status?.toLowerCase() === 'running'" @click="lifecycle.mutate('start')">
                    <Loader2 v-if="lifecycle.isPending.value" class="size-4 animate-spin" />
                    <Layers3 v-else class="size-4" />
                    Start
                  </Button>
                  <Button variant="outline" :disabled="lifecycle.isPending.value || instance?.status?.toLowerCase() !== 'running'" @click="lifecycle.mutate('stop')">
                    <Square class="size-4" />
                    Stop
                  </Button>
                  <Button variant="outline" :disabled="lifecycle.isPending.value || !detail.topology?.config?.allowReset" @click="lifecycle.mutate('reset')">
                    <RefreshCw class="size-4" />
                    Reset
                  </Button>
                  <Button variant="destructive" :disabled="lifecycle.isPending.value || !instance?.id" @click="lifecycle.mutate('destroy')">
                    <Trash2 class="size-4" />
                    Destroy
                  </Button>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>Submit Flag</CardTitle></CardHeader>
              <CardContent class="space-y-3">
                <Input v-model="flag" placeholder="flag{...}" @keyup.enter="submitFlag.mutate()" />
                <Button class="w-full" :disabled="!flag.trim() || submitFlag.isPending.value" @click="submitFlag.mutate()">
                  <Loader2 v-if="submitFlag.isPending.value" class="size-4 animate-spin" />
                  Submit
                </Button>
              </CardContent>
            </Card>
          </aside>
        </div>
      </template>
    </template>
  </div>
</template>
