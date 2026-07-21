<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Bot, Eye, Loader2, Network, RefreshCw, Save, Server, Trash2 } from 'lucide-vue-next'
import AdminCompetitionQqBotDeliveryCenter from '@/ui-v1/components/admin/competition-operations/AdminCompetitionQqBotDeliveryCenter.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import DataState from '@/ui-v1/components/state/DataState.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button, buttonVariants } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Panel } from '@/ui-v1/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/ui-v1/components/ui/select'
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

const { t } = useI18n()
const activeWorkspace = ref('penetration')
const allChallengesValue = '__all__'

const saveTopology = useToastMutation(saveTopologyAction, {
  success: 'admin.competitionOperations.topologySaved',
  error: 'admin.competitionOperations.topologySaveFailed',
})

const instanceAction = useToastMutation<{ id: string, action: 'reset' | 'destroy' }>(instanceActionRunner, {
  success: 'admin.competitionOperations.instanceUpdated',
  error: 'admin.competitionOperations.instanceOperationFailed',
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
