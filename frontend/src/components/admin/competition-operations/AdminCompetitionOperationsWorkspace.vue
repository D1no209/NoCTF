<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { toast } from 'vue-sonner'
import { Bot, Eye, Loader2, Network, RefreshCw, Save, Server, Trash2 } from 'lucide-vue-next'
import { adminApi, penetrationAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionQqBotDeliveryCenter from '@/components/admin/competition-operations/AdminCompetitionQqBotDeliveryCenter.vue'
import DataState from '@/components/state/DataState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'

interface Challenge {
  id: string
  title: string
  typeId: string
}

interface Instance {
  id: string
  teamId?: string
  status?: string
  teamName?: string
  challengeId?: string
  challengeTitle?: string
  entryUrl?: string | null
  expiresAt?: string | null
  resetCount?: number
  lastError?: string | null
}

interface InstanceDetail extends Instance {
  entryHost?: string | null
  entryPort?: number | null
  containerIdsJson?: string | null
  portMappingsJson?: string | null
}

const route = useRoute()
const qc = useQueryClient()
const competitionId = computed(() => String(route.params.id))
const selectedChallengeId = ref('')
const topologyJson = ref('{}')
const instanceChallengeFilter = ref('')
const selectedInstanceId = ref('')

const { data: challenges, isLoading: loadingChallenges } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionChallenges(competitionId.value)),
  queryFn: () => adminApi.competitionChallenges<Challenge[]>(competitionId.value),
})
const penetrationChallenges = computed(() => (challenges.value ?? []).filter(item => item.typeId.toLowerCase().includes('penetration')))
watch(penetrationChallenges, (items) => {
  if (!items.some(item => item.id === selectedChallengeId.value))
    selectedChallengeId.value = items[0]?.id ?? ''
}, { immediate: true })

const topologyKey = computed(() => ['penetration-topology', competitionId.value, selectedChallengeId.value])
const { data: topology, isLoading: loadingTopology, isError: topologyError, refetch: refetchTopology } = useQuery({
  queryKey: topologyKey,
  queryFn: () => penetrationAdminApi.competitionTopology(competitionId.value, selectedChallengeId.value) as unknown as Promise<unknown>,
  enabled: computed(() => Boolean(selectedChallengeId.value)),
})
watch(topology, value => {
  if (value) topologyJson.value = JSON.stringify(value, null, 2)
}, { immediate: true })

const instancesKey = computed(() => ['penetration-instances', competitionId.value])
const { data: instancesRaw, isLoading: loadingInstances, refetch: refetchInstances } = useQuery({
  queryKey: computed(() => [...instancesKey.value, instanceChallengeFilter.value]),
  queryFn: () => penetrationAdminApi.instances(competitionId.value, {
    challengeId: instanceChallengeFilter.value || undefined,
  } as never) as unknown as Promise<Instance[] | { items?: Instance[] }>,
  refetchInterval: 15_000,
})
const instances = computed(() => Array.isArray(instancesRaw.value) ? instancesRaw.value : instancesRaw.value?.items ?? [])
const { data: selectedInstance, isLoading: loadingInstanceDetail } = useQuery({
  queryKey: computed(() => ['penetration-instance-detail', competitionId.value, selectedInstanceId.value]),
  queryFn: () => penetrationAdminApi.instance(competitionId.value, selectedInstanceId.value) as unknown as Promise<InstanceDetail>,
  enabled: computed(() => Boolean(selectedInstanceId.value)),
})

const saveTopology = useMutation({
  mutationFn: () => penetrationAdminApi.updateCompetitionTopology(competitionId.value, selectedChallengeId.value, JSON.parse(topologyJson.value)),
  onSuccess: () => { qc.invalidateQueries({ queryKey: topologyKey.value }); toast.success('Topology saved') },
  onError: () => toast.error('Topology JSON is invalid or could not be saved'),
})
const instanceAction = useMutation({
  mutationFn: ({ id, action }: { id: string, action: 'reset' | 'destroy' }) =>
    action === 'reset'
      ? penetrationAdminApi.resetInstance(competitionId.value, id)
      : penetrationAdminApi.destroyInstance(competitionId.value, id),
  onSuccess: () => { qc.invalidateQueries({ queryKey: instancesKey.value }); toast.success('Instance updated') },
  onError: () => toast.error('Instance operation failed'),
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
