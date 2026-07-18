<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bot, Eye, Loader2, Network, RefreshCw, Save, Server, Trash2 } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { toast } from 'vue-sonner'
import { adminApi, penetrationAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AdminCompetitionQqBotDeliveryCenter from '@/components/admin/competition-operations/AdminCompetitionQqBotDeliveryCenter.vue'
import DataState from '@/components/state/DataState.vue'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
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
const { t } = useI18n()
const competitionId = computed(() => String(route.params.id))
const activeWorkspace = ref('penetration')
const allChallengesValue = '__all__'
const selectedChallengeId = ref('')
const topologyJson = ref('{}')
const instanceChallengeFilter = ref(allChallengesValue)
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
watch(topology, (value) => {
  if (value)
    topologyJson.value = JSON.stringify(value, null, 2)
}, { immediate: true })

const instancesKey = computed(() => ['penetration-instances', competitionId.value])
const { data: instancesRaw, isLoading: loadingInstances, refetch: refetchInstances } = useQuery({
  queryKey: computed(() => [...instancesKey.value, instanceChallengeFilter.value]),
  queryFn: () => penetrationAdminApi.instances(competitionId.value, {
    challengeId: instanceChallengeFilter.value === allChallengesValue ? undefined : instanceChallengeFilter.value,
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
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: topologyKey.value })
    toast.success(t('admin.competitionOperations.topologySaved'))
  },
  onError: () => toast.error(t('admin.competitionOperations.topologySaveFailed')),
})
const instanceAction = useMutation({
  mutationFn: ({ id, action }: { id: string, action: 'reset' | 'destroy' }) =>
    action === 'reset'
      ? penetrationAdminApi.resetInstance(competitionId.value, id)
      : penetrationAdminApi.destroyInstance(competitionId.value, id),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: instancesKey.value })
    toast.success(t('admin.competitionOperations.instanceUpdated'))
  },
  onError: () => toast.error(t('admin.competitionOperations.instanceOperationFailed')),
})
function statusVariant(status?: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status?.toLowerCase() === 'running')
    return 'default'
  if (status?.toLowerCase() === 'failed')
    return 'destructive'
  return 'outline'
}

function setActiveWorkspace(value: 'penetration' | 'qqbot') {
  activeWorkspace.value = value
}
</script>

<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-2xl font-bold tracking-tight">
        {{ t('admin.competitionOperations.title') }}
      </h2>
      <p class="mt-1 text-sm text-muted-foreground">
        {{ t('admin.competitionOperations.description') }}
      </p>
    </div>

    <div class="grid w-full max-w-md grid-cols-2 bg-muted p-1">
      <button type="button" :class="buttonVariants({ variant: activeWorkspace === 'penetration' ? 'secondary' : 'ghost', size: 'sm' })" @click="setActiveWorkspace('penetration')">
        <Network class="size-4" />{{ t('admin.competitionOperations.penetration') }}
      </button>
      <button type="button" :class="buttonVariants({ variant: activeWorkspace === 'qqbot' ? 'secondary' : 'ghost', size: 'sm' })" @click="setActiveWorkspace('qqbot')">
        <Bot class="size-4" />{{ t('admin.qqBot.title') }}
      </button>
    </div>

    <div v-show="activeWorkspace === 'penetration'" class="mt-6 space-y-6">
      <Card>
        <CardHeader><CardTitle>{{ t('admin.competitionOperations.rangeTopology') }}</CardTitle></CardHeader>
        <CardContent class="space-y-4">
          <div v-if="loadingChallenges" class="text-sm text-muted-foreground">
            {{ t('admin.competitionOperations.loadingChallenges') }}
          </div>
          <div v-else-if="!penetrationChallenges.length" class="text-sm text-muted-foreground">
            {{ t('admin.competitionOperations.noPenetrationChallenges') }}
          </div>
          <template v-else>
            <Select v-model="selectedChallengeId">
              <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                <SelectItem v-for="challenge in penetrationChallenges" :key="challenge.id" :value="challenge.id">
                  {{ challenge.title }}
                </SelectItem>
              </SelectContent>
            </Select>
            <DataState v-if="loadingTopology" loading />
            <DataState v-else-if="topologyError" error :retry-label="t('common.retry')" @retry="refetchTopology()" />
            <template v-else>
              <Textarea v-model="topologyJson" rows="20" class="font-mono text-xs" />
              <Button :disabled="saveTopology.isPending.value" @click="saveTopology.mutate()">
                <Loader2 v-if="saveTopology.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.competitionOperations.saveTopology') }}
              </Button>
            </template>
          </template>
        </CardContent>
      </Card>

      <Card>
        <CardHeader class="grid-cols-[minmax(0,1fr)_auto] items-center">
          <CardTitle>{{ t('admin.competitionOperations.rangeInstances') }}</CardTitle><Button variant="outline" size="sm" @click="refetchInstances()">
            <RefreshCw class="size-4" />{{ t('common.refresh') }}
          </Button>
        </CardHeader>
        <CardContent class="space-y-3">
          <Select v-model="instanceChallengeFilter">
            <SelectTrigger><SelectValue :placeholder="t('admin.competitionOperations.allPenetrationChallenges')" /></SelectTrigger>
            <SelectContent>
              <SelectItem :value="allChallengesValue">
                {{ t('admin.competitionOperations.allPenetrationChallenges') }}
              </SelectItem>
              <SelectItem v-for="challenge in penetrationChallenges" :key="challenge.id" :value="challenge.id">
                {{ challenge.title }}
              </SelectItem>
            </SelectContent>
          </Select>
          <div v-if="loadingInstances" class="text-sm text-muted-foreground">
            {{ t('admin.competitionOperations.loadingInstances') }}
          </div>
          <Panel v-for="instance in instances" :key="instance.id" class="flex flex-col gap-3 p-4 md:flex-row md:items-center md:justify-between">
            <div class="min-w-0">
              <div class="flex items-center gap-2">
                <Server class="size-4 text-primary" /><span class="font-medium">{{ instance.teamName ?? t('admin.competitionOperations.teamInstance') }}</span><Badge :variant="statusVariant(instance.status)">
                  {{ instance.status ?? t('admin.competitionOperations.unknown') }}
                </Badge>
              </div><div class="mt-1 truncate text-xs text-muted-foreground">
                {{ instance.challengeTitle }} · {{ instance.entryUrl || t('admin.competitionOperations.noEntryUrl') }}
              </div>
            </div>
            <div class="flex gap-2">
              <Button size="sm" variant="outline" @click="selectedInstanceId = instance.id">
                <Eye class="size-4" />{{ t('admin.competitionOperations.details') }}
              </Button><Button size="sm" variant="outline" :disabled="instanceAction.isPending.value" @click="instanceAction.mutate({ id: instance.id, action: 'reset' })">
                <RefreshCw class="size-4" />{{ t('admin.competitionOperations.reset') }}
              </Button><Button size="sm" variant="destructive" :disabled="instanceAction.isPending.value" @click="instanceAction.mutate({ id: instance.id, action: 'destroy' })">
                <Trash2 class="size-4" />{{ t('admin.competitionOperations.destroy') }}
              </Button>
            </div>
          </Panel>
          <Panel v-if="selectedInstanceId" class="space-y-3 p-4">
            <div class="flex items-center justify-between gap-3">
              <div class="font-medium">
                {{ t('admin.competitionOperations.instanceDetails') }}
              </div><Button size="sm" variant="ghost" @click="selectedInstanceId = ''">
                {{ t('common.close') }}
              </Button>
            </div>
            <div v-if="loadingInstanceDetail" class="text-sm text-muted-foreground">
              {{ t('admin.competitionOperations.loadingInstanceDetails') }}
            </div>
            <template v-else-if="selectedInstance">
              <div class="grid gap-3 text-sm md:grid-cols-2">
                <div>
                  <span class="text-muted-foreground">{{ t('admin.competitionOperations.entry') }}</span><div class="mt-1 break-all font-mono">
                    {{ selectedInstance.entryUrl || selectedInstance.entryHost || t('admin.competitionOperations.unavailable') }}
                  </div>
                </div><div>
                  <span class="text-muted-foreground">{{ t('admin.competitionOperations.resetCount') }}</span><div class="mt-1 font-mono">
                    {{ selectedInstance.resetCount ?? 0 }}
                  </div>
                </div><div>
                  <span class="text-muted-foreground">{{ t('admin.competitionOperations.expires') }}</span><div class="mt-1">
                    {{ selectedInstance.expiresAt ? new Date(selectedInstance.expiresAt).toLocaleString() : t('admin.competitionOperations.notSet') }}
                  </div>
                </div><div>
                  <span class="text-muted-foreground">{{ t('admin.competitionOperations.lastError') }}</span><div class="mt-1 text-destructive">
                    {{ selectedInstance.lastError || t('admin.competitionOperations.none') }}
                  </div>
                </div>
              </div>
              <details v-if="selectedInstance.containerIdsJson || selectedInstance.portMappingsJson" class="border p-3 text-xs">
                <summary class="cursor-pointer font-medium">
                  {{ t('admin.competitionOperations.runtimeMetadata') }}
                </summary><pre class="mt-3 overflow-auto">{{ selectedInstance.containerIdsJson }}{{ selectedInstance.portMappingsJson ? `\n${selectedInstance.portMappingsJson}` : '' }}</pre>
              </details>
            </template>
          </Panel>
          <p v-if="!loadingInstances && !instances.length" class="text-sm text-muted-foreground">
            {{ t('admin.competitionOperations.noActiveInstances') }}
          </p>
        </CardContent>
      </Card>
    </div>

    <div v-show="activeWorkspace === 'qqbot'" class="mt-6">
      <AdminCompetitionQqBotDeliveryCenter :competition-id="competitionId" />
    </div>
  </div>
</template>
