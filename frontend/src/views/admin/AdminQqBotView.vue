<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bot, CheckCircle2, KeyRound, Radio, RefreshCw, Save, ShieldCheck, WifiOff } from 'lucide-vue-next'
import { reactive, watch } from 'vue'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'

interface Settings { enabled: boolean; longPollSeconds: number; deliveryLeaseSeconds: number; maxDeliveryAttempts: number; maxMessageLength: number; maxPendingDeliveries: number; groupCooldownMilliseconds: number; competitionCooldownMilliseconds: number; manualNotificationCooldownSeconds: number }
interface Agent { id: string; name: string; enabled: boolean; apiReachable: boolean; qqOnline: boolean; botUin?: number; botNickname?: string; implementationName?: string; implementationVersion?: string; milkyVersion?: string; lastHeartbeatAt?: string; lastErrorCode?: string; pendingDeliveries: number; recentSucceeded: number; recentFailed: number }
interface Group { id: string; agentId: string; groupId: number; groupName: string; isPresent: boolean; isAuthorized: boolean; lastSeenAt: string }
interface Overview { pluginAvailable: boolean; connectionType: string; settings: Settings; agents: Agent[]; groups: Group[] }

const queryClient = useQueryClient()
const form = reactive<Settings>({ enabled: false, longPollSeconds: 25, deliveryLeaseSeconds: 60, maxDeliveryAttempts: 5, maxMessageLength: 2000, maxPendingDeliveries: 10000, groupCooldownMilliseconds: 1000, competitionCooldownMilliseconds: 250, manualNotificationCooldownSeconds: 10 })
const agentForm = reactive({ id: undefined as string | undefined, name: '', enabled: true, publicKeyPem: '', previousKeyOverlapMinutes: 60 })
const query = useQuery({ queryKey: queryKeys.adminQqBot, queryFn: () => adminApi.qqBotOverview<Overview>(), refetchInterval: 15000 })
watch(() => query.data.value?.settings, value => { if (value) Object.assign(form, value) }, { immediate: true })
const save = useMutation({ mutationFn: () => adminApi.updateQqBotSettings(form), onSuccess: async () => { toast.success('QQ 机器人全局设置已保存'); await queryClient.invalidateQueries({ queryKey: queryKeys.adminQqBot }) } })
const saveAgent = useMutation({ mutationFn: () => adminApi.upsertQqBotAgent(agentForm), onSuccess: async () => { toast.success('Agent 已保存，请将私钥仅部署在 BOT 主机'); agentForm.publicKeyPem = ''; agentForm.name = ''; agentForm.id = undefined; await queryClient.invalidateQueries({ queryKey: queryKeys.adminQqBot }) } })
const authorize = useMutation({ mutationFn: ({ id, value }: { id: string; value: boolean }) => adminApi.authorizeQqBotGroup(id, value), onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.adminQqBot }) })
</script>

<template>
  <div class="noctf-admin-page space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div><div class="flex items-center gap-2"><Bot class="size-6 text-primary" /><h2 class="text-2xl font-bold tracking-tight">QQ 机器人</h2></div><p class="mt-1 text-sm text-muted-foreground">BOT 主动通过 HTTPS 长轮询连接平台；浏览器与平台均不保存 BOT 私钥。</p></div>
      <Button variant="outline" :disabled="query.isFetching.value" @click="query.refetch()"><RefreshCw class="mr-2 size-4" />刷新状态</Button>
    </div>

    <div class="noctf-status-strip grid-cols-1 md:grid-cols-3">
      <div class="noctf-status-item"><Radio class="size-4" /><span class="noctf-label">插件</span><Badge :variant="form.enabled ? 'success' : 'neutral'">{{ form.enabled ? '已启用' : '已关闭' }}</Badge></div>
      <div class="noctf-status-item"><component :is="query.data.value?.agents.some(a => a.apiReachable) ? CheckCircle2 : WifiOff" class="size-4" /><span class="noctf-label">Agent API</span><span class="font-mono text-xs">{{ query.data.value?.agents.some(a => a.apiReachable) ? 'reachable' : 'offline' }}</span></div>
      <div class="noctf-status-item"><ShieldCheck class="size-4" /><span class="noctf-label">连接模式</span><span class="font-mono text-xs">outbound HTTPS</span></div>
    </div>

    <section class="noctf-workbench p-5">
      <div class="mb-5"><h3 class="font-semibold">发送与租约策略</h3><p class="text-xs text-muted-foreground">关闭全局开关不会影响比赛核心业务，只会抑制新的机器人投递。</p></div>
      <div class="grid gap-4 md:grid-cols-3">
        <label class="flex items-center gap-3 rounded-md border p-3"><input v-model="form.enabled" type="checkbox" class="size-4" /><span class="text-sm font-medium">启用插件</span></label>
        <div><Label>长轮询（秒）</Label><Input v-model.number="form.longPollSeconds" type="number" min="1" max="30" /></div>
        <div><Label>投递租约（秒）</Label><Input v-model.number="form.deliveryLeaseSeconds" type="number" min="15" max="300" /></div>
        <div><Label>最大尝试次数</Label><Input v-model.number="form.maxDeliveryAttempts" type="number" min="1" max="10" /></div>
        <div><Label>最大消息长度</Label><Input v-model.number="form.maxMessageLength" type="number" min="100" max="4000" /></div>
        <div><Label>最大待发送数</Label><Input v-model.number="form.maxPendingDeliveries" type="number" min="100" /></div>
        <div><Label>单群冷却（毫秒）</Label><Input v-model.number="form.groupCooldownMilliseconds" type="number" min="0" /></div>
        <div><Label>单比赛冷却（毫秒）</Label><Input v-model.number="form.competitionCooldownMilliseconds" type="number" min="0" /></div>
        <div><Label>人工通知冷却（秒）</Label><Input v-model.number="form.manualNotificationCooldownSeconds" type="number" min="1" /></div>
      </div>
      <div class="mt-5 flex justify-end"><Button :disabled="save.isPending.value" @click="save.mutate()"><Save class="mr-2 size-4" />保存设置</Button></div>
    </section>

    <section class="noctf-workbench p-5">
      <div class="mb-4 flex items-center gap-2"><KeyRound class="size-4" /><h3 class="font-semibold">注册或轮换 Agent 公钥</h3></div>
      <div class="grid gap-4 md:grid-cols-2"><div><Label>Agent 名称</Label><Input v-model="agentForm.name" maxlength="80" placeholder="例如：主赛 BOT" /></div><div><Label>旧公钥重叠时间（分钟）</Label><Input v-model.number="agentForm.previousKeyOverlapMinutes" type="number" min="0" max="1440" /></div><div class="md:col-span-2"><Label>ECDSA P-256 公钥 PEM</Label><Textarea v-model="agentForm.publicKeyPem" class="min-h-36 font-mono text-xs" placeholder="-----BEGIN PUBLIC KEY-----" /></div></div>
      <div class="mt-4 flex justify-end"><Button :disabled="saveAgent.isPending.value || !agentForm.name || !agentForm.publicKeyPem" @click="saveAgent.mutate()">保存 Agent</Button></div>
    </section>

    <section class="noctf-workbench overflow-hidden">
      <div class="border-b p-5"><h3 class="font-semibold">运行状态</h3><p class="text-xs text-muted-foreground">“平台可达”和“QQ 已登录”分别展示，避免把接口连通误认为机器人在线。</p></div>
      <div v-if="!query.data.value?.agents.length" class="noctf-state-box py-12">尚未注册 Agent</div>
      <div v-for="agent in query.data.value?.agents" :key="agent.id" class="grid gap-3 border-b p-4 md:grid-cols-[1fr_auto]">
        <div><div class="font-semibold">{{ agent.name }}</div><div class="mt-1 text-xs text-muted-foreground">{{ agent.botNickname || 'QQ 未登录' }} · {{ agent.botUin || '—' }} · {{ agent.implementationName || 'Milky' }} {{ agent.implementationVersion }}</div><div v-if="agent.lastErrorCode" class="mt-1 text-xs text-destructive">{{ agent.lastErrorCode }}</div></div>
        <div class="flex flex-wrap items-center gap-2"><Badge :variant="agent.apiReachable ? 'success' : 'neutral'">API {{ agent.apiReachable ? '在线' : '离线' }}</Badge><Badge :variant="agent.qqOnline ? 'success' : 'neutral'">QQ {{ agent.qqOnline ? '在线' : '离线' }}</Badge><span class="font-mono text-xs">待发 {{ agent.pendingDeliveries }} / 成功 {{ agent.recentSucceeded }} / 失败 {{ agent.recentFailed }}</span></div>
      </div>
    </section>

    <section class="noctf-workbench overflow-hidden">
      <div class="border-b p-5"><h3 class="font-semibold">群聊授权</h3><p class="text-xs text-muted-foreground">只有 Agent 实际加入、同步成功并在此授权的群，才能绑定比赛。</p></div>
      <div v-if="!query.data.value?.groups.length" class="noctf-state-box py-12">等待 Agent 同步群列表</div>
      <div v-for="group in query.data.value?.groups" :key="group.id" class="flex items-center justify-between gap-4 border-b p-4"><div><div class="font-semibold">{{ group.groupName }}</div><div class="font-mono text-xs text-muted-foreground">{{ group.groupId }}</div></div><Button size="sm" :variant="group.isAuthorized ? 'destructive' : 'outline'" :disabled="!group.isPresent || authorize.isPending.value" @click="authorize.mutate({ id: group.id, value: !group.isAuthorized })">{{ group.isAuthorized ? '撤销授权' : '授权群聊' }}</Button></div>
    </section>
  </div>
</template>
