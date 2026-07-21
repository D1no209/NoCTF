<script setup lang="ts">
import { Bot, Eye, Loader2, Network, RefreshCw, Save, Server, Trash2 } from 'lucide-vue-next'
import AdminCompetitionQqBotDeliveryCenter from '@/ui-v1/components/admin/competition-operations/AdminCompetitionQqBotDeliveryCenter.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import DataState from '@/ui-v1/components/state/DataState.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Panel } from '@/ui-v1/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/ui-v1/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/ui-v1/components/ui/tabs'
import { Textarea } from '@/ui-v1/components/ui/textarea'
import { useAdminCompetitionOperationsPage } from '@/features/admin/useAdminCompetitionOperationsPage'

const {
  competitionId,
  selectedChallengeId,
  topologyJson,
  instanceChallengeFilter,
  selectedInstanceId,
  penetrationChallenges,
  loadingChallenges,
  loadingTopology,
  topologyError,
  refetchTopology,
  instances,
  loadingInstances,
  refetchInstances,
  selectedInstance,
  loadingInstanceDetail,
  saveTopologyMutation: saveTopologyAction,
  instanceActionMutation: instanceActionRunner,
} = useAdminCompetitionOperationsPage()

const saveTopology = useToastMutation(saveTopologyAction, {
  success: 'Topology saved',
  error: 'Topology JSON is invalid or could not be saved',
})

const instanceAction = useToastMutation<{ id: string, action: 'reset' | 'destroy' }>(instanceActionRunner, {
  success: 'Instance updated',
  error: 'Instance operation failed',
})

function statusVariant(status?: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status?.toLowerCase() === 'running') return 'default'
  if (status?.toLowerCase() === 'failed') return 'destructive'
  return 'outline'
}
</script>

<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-2xl font-bold tracking-tight">Competition operations</h2>
      <p class="mt-1 text-sm text-muted-foreground">Range topology, running instances, and competition QQ Bot delivery.</p>
    </div>

    <Tabs default-value="penetration">
      <TabsList class="grid w-full max-w-md grid-cols-2">
        <TabsTrigger value="penetration"><Network class="size-4" />Penetration</TabsTrigger>
        <TabsTrigger value="qqbot"><Bot class="size-4" />QQ Bot</TabsTrigger>
      </TabsList>

      <TabsContent value="penetration" class="mt-6 space-y-6">
        <Card>
          <CardHeader><CardTitle>Range topology</CardTitle></CardHeader>
          <CardContent class="space-y-4">
            <div v-if="loadingChallenges" class="text-sm text-muted-foreground">Loading challenges…</div>
            <div v-else-if="!penetrationChallenges.length" class="text-sm text-muted-foreground">No penetration challenge is published for this competition.</div>
            <template v-else>
              <Select v-model="selectedChallengeId"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="challenge in penetrationChallenges" :key="challenge.id" :value="challenge.id">{{ challenge.title }}</SelectItem></SelectContent></Select>
              <DataState v-if="loadingTopology" loading />
              <DataState v-else-if="topologyError" error retry-label="Retry" @retry="refetchTopology()" />
              <template v-else>
                <Textarea v-model="topologyJson" rows="20" class="font-mono text-xs" />
                <Button :disabled="saveTopology.isPending.value" @click="saveTopology.mutate()"><Loader2 v-if="saveTopology.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />Save topology</Button>
              </template>
            </template>
          </CardContent>
        </Card>

        <Card>
          <CardHeader class="flex-row items-center justify-between"><CardTitle>Range instances</CardTitle><Button variant="outline" size="sm" @click="refetchInstances()"><RefreshCw class="size-4" />Refresh</Button></CardHeader>
          <CardContent class="space-y-3">
            <Select v-model="instanceChallengeFilter">
              <SelectTrigger><SelectValue placeholder="All penetration challenges" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="">All penetration challenges</SelectItem>
                <SelectItem v-for="challenge in penetrationChallenges" :key="challenge.id" :value="challenge.id">{{ challenge.title }}</SelectItem>
              </SelectContent>
            </Select>
            <div v-if="loadingInstances" class="text-sm text-muted-foreground">Loading instances…</div>
            <Panel v-for="instance in instances" :key="instance.id" class="flex flex-col gap-3 p-4 md:flex-row md:items-center md:justify-between">
              <div class="min-w-0"><div class="flex items-center gap-2"><Server class="size-4 text-primary" /><span class="font-medium">{{ instance.teamName ?? 'Team instance' }}</span><Badge :variant="statusVariant(instance.status)">{{ instance.status ?? 'Unknown' }}</Badge></div><div class="mt-1 truncate text-xs text-muted-foreground">{{ instance.challengeTitle }} · {{ instance.entryUrl || 'No entry URL' }}</div></div>
              <div class="flex gap-2"><Button size="sm" variant="outline" @click="selectedInstanceId = instance.id"><Eye class="size-4" />Details</Button><Button size="sm" variant="outline" :disabled="instanceAction.isPending.value" @click="instanceAction.mutate({ id: instance.id, action: 'reset' })"><RefreshCw class="size-4" />Reset</Button><Button size="sm" variant="destructive" :disabled="instanceAction.isPending.value" @click="instanceAction.mutate({ id: instance.id, action: 'destroy' })"><Trash2 class="size-4" />Destroy</Button></div>
            </Panel>
            <Panel v-if="selectedInstanceId" class="space-y-3 p-4">
              <div class="flex items-center justify-between gap-3"><div class="font-medium">Instance details</div><Button size="sm" variant="ghost" @click="selectedInstanceId = ''">Close</Button></div>
              <div v-if="loadingInstanceDetail" class="text-sm text-muted-foreground">Loading instance details...</div>
              <template v-else-if="selectedInstance">
                <div class="grid gap-3 text-sm md:grid-cols-2"><div><span class="text-muted-foreground">Entry</span><div class="mt-1 break-all font-mono">{{ selectedInstance.entryUrl || selectedInstance.entryHost || 'Unavailable' }}</div></div><div><span class="text-muted-foreground">Reset count</span><div class="mt-1 font-mono">{{ selectedInstance.resetCount ?? 0 }}</div></div><div><span class="text-muted-foreground">Expires</span><div class="mt-1">{{ selectedInstance.expiresAt ? new Date(selectedInstance.expiresAt).toLocaleString() : 'Not set' }}</div></div><div><span class="text-muted-foreground">Last error</span><div class="mt-1 text-destructive">{{ selectedInstance.lastError || 'None' }}</div></div></div>
                <details v-if="selectedInstance.containerIdsJson || selectedInstance.portMappingsJson" class="border p-3 text-xs"><summary class="cursor-pointer font-medium">Runtime metadata</summary><pre class="mt-3 overflow-auto">{{ selectedInstance.containerIdsJson }}{{ selectedInstance.portMappingsJson ? '\n' + selectedInstance.portMappingsJson : '' }}</pre></details>
              </template>
            </Panel>
            <p v-if="!loadingInstances && !instances.length" class="text-sm text-muted-foreground">No penetration instances are active.</p>
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="qqbot" class="mt-6">
        <AdminCompetitionQqBotDeliveryCenter :competition-id="competitionId" />
      </TabsContent>
    </Tabs>
  </div>
</template>
