<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Eye, FileText, Loader2, RefreshCw, RotateCcw, Save, Send } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import DataState from '@/components/state/DataState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

type QqEventType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7

interface QqRule {
  eventType: QqEventType
  enabled: boolean
  templateId?: string | null
}

interface QqBinding {
  agentId: string
  groupId: string
  qqGroupId: number
  groupName?: string
  isDefault?: boolean
  eventTypes?: QqEventType[]
}

interface QqConfig {
  enabled: boolean
  allowMessages: boolean
  allowManualNotifications: boolean
  stopNormalEventsAfterFinished: boolean
  mentionAll: boolean
  showTeamName: boolean
  showUserName: boolean
  showChallengeCategory: boolean
  includeCompetitionLink: boolean
  includeChallengeLink: boolean
  hidePenaltyDetails: boolean
  eventRules: QqRule[]
  groupBindings: QqBinding[]
  warnings: string[]
}

interface QqGroup {
  id: string
  agentId: string
  groupId: number
  groupName: string
}

interface QqTemplate {
  id?: string | null
  eventType?: QqEventType
  name?: string
  content?: string
  isDefault?: boolean
}

interface QqPreview {
  renderedText?: string
  characterCount?: number
  warnings?: string[]
}

interface QqDelivery {
  id?: string
  eventType?: QqEventType
  groupName?: string
  status?: number
  attemptCount?: number
  maxAttempts?: number
  messageSummary?: string
  lastErrorSummary?: string | null
}

const props = defineProps<{ competitionId: string }>()
const qc = useQueryClient()
const selectedTemplateId = ref('')
const preview = ref<QqPreview | null>(null)
const selectedGroupIds = ref<string[]>([])
const templateForm = reactive({ id: null as string | null, eventType: 0 as QqEventType, name: '', content: '', isDefault: false })
const announcementForm = reactive({ title: '', content: '', isTest: false })
const form = reactive<QqConfig>({
  enabled: false,
  allowMessages: true,
  allowManualNotifications: false,
  stopNormalEventsAfterFinished: true,
  mentionAll: false,
  showTeamName: true,
  showUserName: false,
  showChallengeCategory: true,
  includeCompetitionLink: false,
  includeChallengeLink: false,
  hidePenaltyDetails: true,
  eventRules: [],
  groupBindings: [],
  warnings: [],
})

const eventLabels: Record<QqEventType, string> = {
  0: 'Competition start',
  1: 'Competition end',
  2: 'Challenge solved',
  3: 'First blood',
  4: 'Team penalty',
  5: 'System notice',
  6: 'Manual announcement',
  7: 'Scoreboard update',
}

const { data: config, isLoading, isError, refetch } = useQuery({
  queryKey: computed(() => ['competition-qqbot', props.competitionId]),
  queryFn: () => adminApi.competitionQqBot(props.competitionId) as Promise<QqConfig>,
})
const { data: templates, isLoading: loadingTemplates } = useQuery({
  queryKey: computed(() => ['competition-qqbot-templates', props.competitionId]),
  queryFn: () => adminApi.competitionQqBotTemplates(props.competitionId) as Promise<QqTemplate[]>,
})
const { data: availableGroups } = useQuery({
  queryKey: computed(() => ['competition-qqbot-groups', props.competitionId]),
  queryFn: () => adminApi.availableQqBotGroups(props.competitionId) as Promise<QqGroup[]>,
  retry: false,
})
const { data: deliveryLog, refetch: refetchDeliveryLog } = useQuery({
  queryKey: computed(() => ['competition-qqbot-log', props.competitionId]),
  queryFn: () => adminApi.qqBotLogs(props.competitionId, {} as never) as Promise<{ items?: QqDelivery[] }>,
  refetchInterval: 15_000,
})

const groupsById = computed(() => new Map((availableGroups.value ?? []).map(group => [group.id, group])))
const bindings = computed<QqBinding[]>(() => selectedGroupIds.value.flatMap((groupId) => {
  const existing = form.groupBindings.find(binding => binding.groupId === groupId)
  if (existing)
    return [existing]
  const group = groupsById.value.get(groupId)
  return group ? [{
    agentId: group.agentId,
    groupId: group.id,
    qqGroupId: group.groupId,
    groupName: group.groupName,
    isDefault: true,
    eventTypes: [],
  }] : []
}))
const deliveryItems = computed(() => deliveryLog.value?.items ?? [])

watch(config, (value) => {
  if (!value)
    return
  Object.assign(form, {
    enabled: value.enabled,
    allowMessages: value.allowMessages,
    allowManualNotifications: value.allowManualNotifications,
    stopNormalEventsAfterFinished: value.stopNormalEventsAfterFinished,
    mentionAll: value.mentionAll,
    showTeamName: value.showTeamName,
    showUserName: value.showUserName,
    showChallengeCategory: value.showChallengeCategory,
    includeCompetitionLink: value.includeCompetitionLink,
    includeChallengeLink: value.includeCompetitionLink,
    hidePenaltyDetails: value.hidePenaltyDetails,
    eventRules: value.eventRules ?? [],
    groupBindings: value.groupBindings ?? [],
    warnings: value.warnings ?? [],
  })
  selectedGroupIds.value = (value.groupBindings ?? []).map(binding => binding.groupId)
}, { immediate: true })

watch(templates, (items) => {
  if (!selectedTemplateId.value)
    selectedTemplateId.value = items?.[0]?.id ?? ''
}, { immediate: true })

function toggleGroup(groupId: string) {
  selectedGroupIds.value = selectedGroupIds.value.includes(groupId)
    ? selectedGroupIds.value.filter(id => id !== groupId)
    : [...selectedGroupIds.value, groupId]
}

function selectTemplate(template: QqTemplate) {
  templateForm.id = template.id ?? null
  templateForm.eventType = template.eventType ?? 0
  templateForm.name = template.name ?? ''
  templateForm.content = template.content ?? ''
  templateForm.isDefault = template.isDefault ?? false
  selectedTemplateId.value = template.id ?? ''
}

function resetTemplate() {
  templateForm.id = null
  templateForm.eventType = 0
  templateForm.name = ''
  templateForm.content = ''
  templateForm.isDefault = false
}

function statusLabel(status?: number) {
  return ['Queued', 'Leased', 'Sent', 'Failed', 'Cancelled', 'Retrying'][status ?? 0] ?? 'Unknown'
}

function statusVariant(status?: number): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 2)
    return 'default'
  if (status === 3 || status === 4)
    return 'destructive'
  return 'outline'
}

const saveConfig = useMutation({
  mutationFn: () => adminApi.updateCompetitionQqBot(props.competitionId, { ...form, groupBindings: bindings.value }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['competition-qqbot', props.competitionId] })
    toast.success('QQ Bot configuration saved')
  },
  onError: () => toast.error('Unable to save QQ Bot configuration'),
})
const saveTemplate = useMutation({
  mutationFn: () => adminApi.upsertCompetitionQqBotTemplate(props.competitionId, {
    id: templateForm.id,
    eventType: templateForm.eventType,
    name: templateForm.name.trim(),
    content: templateForm.content,
    isDefault: templateForm.isDefault,
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['competition-qqbot-templates', props.competitionId] })
    toast.success('Template saved')
  },
  onError: () => toast.error('Unable to save the template'),
})
const previewTemplate = useMutation({
  mutationFn: () => adminApi.previewQqBot(props.competitionId, {
    eventType: templateForm.eventType,
    templateId: templateForm.id,
    announcementTitle: announcementForm.title.trim() || null,
    announcementContent: announcementForm.content.trim() || null,
  }),
  onSuccess: value => { preview.value = value as QqPreview },
  onError: () => toast.error('Unable to render the preview'),
})
const sendAnnouncement = useMutation({
  mutationFn: () => adminApi.sendQqBotNotification(props.competitionId, {
    title: announcementForm.title.trim(),
    content: announcementForm.content.trim(),
    templateId: selectedTemplateId.value || null,
    groupIds: bindings.value.map(binding => binding.groupId),
    isTest: announcementForm.isTest,
  }),
  onSuccess: () => {
    announcementForm.title = ''
    announcementForm.content = ''
    qc.invalidateQueries({ queryKey: ['competition-qqbot-log', props.competitionId] })
    toast.success('Notification queued')
  },
  onError: () => toast.error('Unable to queue the notification'),
})
const retryDelivery = useMutation({
  mutationFn: (deliveryId: string) => adminApi.retryQqBotDelivery(props.competitionId, deliveryId),
  onSuccess: () => {
    refetchDeliveryLog()
    toast.success('Delivery queued for retry')
  },
  onError: () => toast.error('Unable to retry this delivery'),
})
</script>

<template>
  <DataState v-if="isLoading" loading />
  <DataState v-else-if="isError" error retry-label="Retry" @retry="refetch()" />

  <div v-else class="space-y-6">
    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4">
        <div><CardTitle>Delivery configuration</CardTitle><p class="mt-1 text-sm text-muted-foreground">Event rules and target groups for this competition.</p></div>
        <Button variant="outline" size="sm" @click="refetch()"><RefreshCw class="size-4" />Refresh</Button>
      </CardHeader>
      <CardContent class="space-y-5">
        <div v-if="form.warnings.length" class="border border-amber-500/30 bg-amber-500/5 p-3 text-sm text-amber-800">{{ form.warnings.join(' ') }}</div>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <label v-for="item in [
            ['enabled', 'Enable delivery'], ['allowMessages', 'Allow messages'], ['allowManualNotifications', 'Allow manual announcements'],
            ['stopNormalEventsAfterFinished', 'Stop normal events after finish'], ['mentionAll', 'Mention all'], ['showTeamName', 'Show team name'],
            ['showUserName', 'Show user name'], ['showChallengeCategory', 'Show challenge category'], ['includeCompetitionLink', 'Include competition link'],
            ['includeChallengeLink', 'Include challenge link'], ['hidePenaltyDetails', 'Hide penalty details'],
          ]" :key="item[0]" class="flex items-center justify-between border p-3 text-sm"><span>{{ item[1] }}</span><input v-model="form[item[0] as keyof QqConfig] as boolean" type="checkbox" class="size-4"></label>
        </div>
        <div class="grid gap-5 xl:grid-cols-2">
          <div class="space-y-2"><Label>Bound groups</Label><div v-if="availableGroups?.length" class="grid gap-2 sm:grid-cols-2"><Button v-for="group in availableGroups" :key="group.id" variant="outline" class="h-auto justify-start p-3 text-left" :class="selectedGroupIds.includes(group.id) && 'border-primary bg-primary/5'" @click="toggleGroup(group.id)"><span class="min-w-0"><span class="block truncate font-medium">{{ group.groupName }}</span><span class="block text-xs text-muted-foreground">{{ group.groupId }}</span></span></Button></div><p v-else class="text-sm text-muted-foreground">No additional authorized group is available to this account.</p><div v-if="bindings.length" class="flex flex-wrap gap-2"><Badge v-for="binding in bindings" :key="binding.groupId" variant="secondary">{{ binding.groupName ?? binding.qqGroupId }}</Badge></div></div>
          <div class="space-y-2"><Label>Event rules</Label><Panel v-for="rule in form.eventRules" :key="rule.eventType" class="flex items-center justify-between gap-3 p-3"><span>{{ eventLabels[rule.eventType] }}</span><input v-model="rule.enabled" type="checkbox" class="size-4"></Panel></div>
        </div>
        <Button :disabled="saveConfig.isPending.value" @click="saveConfig.mutate()"><Loader2 v-if="saveConfig.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />Save configuration</Button>
      </CardContent>
    </Card>

    <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
      <Card>
        <CardHeader class="flex-row items-center justify-between gap-4"><div><CardTitle class="flex items-center gap-2"><FileText class="size-5 text-primary" />Message templates</CardTitle><p class="mt-1 text-sm text-muted-foreground">Create event templates and inspect the rendered result.</p></div><Button variant="outline" size="sm" @click="resetTemplate">New</Button></CardHeader>
        <CardContent class="space-y-4">
          <div v-if="loadingTemplates" class="text-sm text-muted-foreground">Loading templates...</div>
          <div v-else-if="templates?.length" class="flex flex-wrap gap-2"><Button v-for="template in templates" :key="template.id ?? template.name" size="sm" :variant="templateForm.id === template.id ? 'default' : 'outline'" @click="selectTemplate(template)">{{ template.name || eventLabels[template.eventType ?? 0] }}</Button></div>
          <div class="grid gap-3 md:grid-cols-2"><div class="grid gap-2"><Label>Event</Label><Select v-model="templateForm.eventType"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="(_, eventType) in eventLabels" :key="eventType" :value="Number(eventType)">{{ eventLabels[Number(eventType) as QqEventType] }}</SelectItem></SelectContent></Select></div><div class="grid gap-2"><Label>Name</Label><Input v-model="templateForm.name" placeholder="Template name" /></div></div>
          <div class="grid gap-2"><Label>Content</Label><Textarea v-model="templateForm.content" rows="10" class="font-mono text-xs" placeholder="Use the variables allowed by this event." /></div>
          <label class="flex items-center gap-2 text-sm"><input v-model="templateForm.isDefault" type="checkbox" class="size-4">Use as the event default</label>
          <div class="flex flex-wrap gap-2"><Button :disabled="!templateForm.name.trim() || !templateForm.content.trim() || saveTemplate.isPending.value" @click="saveTemplate.mutate()"><Loader2 v-if="saveTemplate.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />Save template</Button><Button variant="outline" :disabled="previewTemplate.isPending.value" @click="previewTemplate.mutate()"><Loader2 v-if="previewTemplate.isPending.value" class="size-4 animate-spin" /><Eye v-else class="size-4" />Preview</Button></div>
          <Panel v-if="preview" class="space-y-2 p-4"><div class="flex items-center justify-between text-sm"><span class="font-medium">Preview</span><Badge variant="outline">{{ preview.characterCount ?? 0 }} chars</Badge></div><pre class="whitespace-pre-wrap break-words text-sm">{{ preview.renderedText }}</pre><p v-if="preview.warnings?.length" class="text-xs text-amber-700">{{ preview.warnings.join(' ') }}</p></Panel>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle class="flex items-center gap-2"><Send class="size-5 text-primary" />Manual announcement</CardTitle></CardHeader>
        <CardContent class="space-y-4"><div class="grid gap-2"><Label>Title</Label><Input v-model="announcementForm.title" placeholder="Announcement title" /></div><div class="grid gap-2"><Label>Content</Label><Textarea v-model="announcementForm.content" rows="8" placeholder="Message content" /></div><div class="grid gap-2"><Label>Template</Label><Select v-model="selectedTemplateId"><SelectTrigger><SelectValue placeholder="No template" /></SelectTrigger><SelectContent><SelectItem value="">No template</SelectItem><SelectItem v-for="template in templates ?? []" :key="template.id ?? template.name ?? `event-${template.eventType ?? 0}`" :value="template.id ?? ''">{{ template.name || eventLabels[template.eventType ?? 0] }}</SelectItem></SelectContent></Select></div><p class="text-xs text-muted-foreground">{{ bindings.length }} bound group{{ bindings.length === 1 ? '' : 's' }} will receive this message.</p><label class="flex items-center gap-2 text-sm"><input v-model="announcementForm.isTest" type="checkbox" class="size-4">Send as a test</label><Button class="w-full" :disabled="!announcementForm.title.trim() || !announcementForm.content.trim() || !bindings.length || sendAnnouncement.isPending.value" @click="sendAnnouncement.mutate()"><Loader2 v-if="sendAnnouncement.isPending.value" class="size-4 animate-spin" /><Send v-else class="size-4" />Queue announcement</Button></CardContent>
      </Card>
    </div>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4"><div><CardTitle>Delivery log</CardTitle><p class="mt-1 text-sm text-muted-foreground">Recent delivery attempts for this competition.</p></div><Button variant="outline" size="sm" @click="refetchDeliveryLog()"><RefreshCw class="size-4" />Refresh</Button></CardHeader>
      <CardContent class="space-y-2"><Panel v-for="delivery in deliveryItems" :key="delivery.id" class="flex flex-col gap-3 p-4 md:flex-row md:items-center md:justify-between"><div class="min-w-0"><div class="flex flex-wrap items-center gap-2"><span class="font-medium">{{ delivery.groupName || 'Unknown group' }}</span><Badge :variant="statusVariant(delivery.status)">{{ statusLabel(delivery.status) }}</Badge><span class="text-xs text-muted-foreground">{{ eventLabels[delivery.eventType ?? 0] }}</span></div><p class="mt-1 truncate text-sm text-muted-foreground">{{ delivery.messageSummary }}</p><p v-if="delivery.lastErrorSummary" class="mt-1 text-xs text-destructive">{{ delivery.lastErrorSummary }}</p></div><div class="flex items-center gap-3"><span class="text-xs tabular-nums text-muted-foreground">{{ delivery.attemptCount ?? 0 }} / {{ delivery.maxAttempts ?? 0 }}</span><Button v-if="delivery.id && delivery.status === 3" size="sm" variant="outline" :disabled="retryDelivery.isPending.value" @click="retryDelivery.mutate(delivery.id)"><RotateCcw class="size-4" />Retry</Button></div></Panel><p v-if="!deliveryItems.length" class="py-4 text-sm text-muted-foreground">No deliveries have been recorded yet.</p></CardContent>
    </Card>
  </div>
</template>
