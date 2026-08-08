<script setup lang="ts">
import { Bot, Copy, KeyRound, Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformCreateBot,
  adminPlatformIssueBotToken,
  adminPlatformListUsers,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformIssuePlatformBotTokenResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse,
  NoCtfapiEndpointsAuthenticationUserRoleProtocol,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse

const ROLE_LABELS: Record<string, string> = { User: '用户', Organizer: '组织者', Administrator: '管理员' }

const bots = ref<PlatformUser[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminPlatformListUsers()
  loading.value = false
  if (error) {
    loadError.value = parseApiError(error).message
    return
  }
  bots.value = (data?.items ?? []).filter(user => user.kind === 'Bot')
}

// ---------- 创建 ----------
const createOpen = ref(false)
const creating = ref(false)
const botName = ref('')
const botRole = ref<NoCtfapiEndpointsAuthenticationUserRoleProtocol>('User')

function openCreate(): void {
  botName.value = ''
  botRole.value = 'User'
  createOpen.value = true
}

async function createBot(): Promise<void> {
  if (!botName.value.trim()) return
  creating.value = true
  const { error } = await adminPlatformCreateBot({
    body: { userName: botName.value.trim(), role: botRole.value },
  })
  creating.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  createOpen.value = false
  toast.success('Bot 已创建')
  await load()
}

// ---------- 签发令牌 ----------
const issueOpen = ref(false)
const issuing = ref(false)
const issueTarget = ref<PlatformUser | null>(null)
const expiresInSeconds = ref(3600)
const issuedToken = ref<NoCtfapiEndpointsAdministrationPlatformIssuePlatformBotTokenResponse | null>(null)

function openIssue(bot: PlatformUser): void {
  issueTarget.value = bot
  expiresInSeconds.value = 3600
  issuedToken.value = null
  issueOpen.value = true
}

async function issueToken(): Promise<void> {
  if (!issueTarget.value?.id) return
  issuing.value = true
  const { data, error } = await adminPlatformIssueBotToken({
    path: { userId: issueTarget.value.id },
    body: { expiresInSeconds: expiresInSeconds.value },
  })
  issuing.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  issuedToken.value = data ?? null
}

async function copyToken(): Promise<void> {
  if (!issuedToken.value?.accessToken) return
  try {
    await navigator.clipboard.writeText(issuedToken.value.accessToken)
    toast.success('已复制到剪贴板')
  }
  catch {
    toast.error('复制失败,请手动选择复制')
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-4">
      <p class="text-sm text-muted-foreground">Bot 是用于 API 集成的服务账户,通过签发的访问令牌调用接口。</p>
      <Button @click="openCreate">
        <Plus data-icon="inline-start" />
        创建 Bot
      </Button>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <Card v-if="loading">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="bots.length === 0">
      <EmptyHeader>
        <EmptyTitle>暂无 Bot</EmptyTitle>
        <EmptyDescription>创建 Bot 服务账户并为其签发访问令牌。</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>名称</TableHead>
            <TableHead>角色</TableHead>
            <TableHead>创建时间</TableHead>
            <TableHead class="text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="bot in bots" :key="bot.id">
            <TableCell class="font-medium">
              <span class="inline-flex items-center gap-2">
                <Bot class="size-4 text-muted-foreground" />
                {{ bot.userName }}
              </span>
            </TableCell>
            <TableCell>
              <Badge :variant="bot.role === 'Administrator' ? 'default' : 'secondary'">
                {{ ROLE_LABELS[String(bot.role)] ?? bot.role }}
              </Badge>
            </TableCell>
            <TableCell>
              <AdminDateTime :value="bot.createdAt" />
            </TableCell>
            <TableCell class="text-right">
              <Button size="sm" variant="outline" @click="openIssue(bot)">
                <KeyRound data-icon="inline-start" />
                签发令牌
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <Dialog v-model:open="createOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>创建 Bot</DialogTitle>
          <DialogDescription>Bot 是特殊的服务账户,创建后可为其签发访问令牌。</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="bot-name">名称</FieldLabel>
            <Input id="bot-name" v-model="botName" required maxlength="50" placeholder="例如:scoreboard-sync" />
          </Field>
          <Field>
            <FieldLabel for="bot-role">角色</FieldLabel>
            <Select v-model="botRole">
              <SelectTrigger id="bot-role" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem value="User">用户</SelectItem>
                  <SelectItem value="Organizer">组织者</SelectItem>
                  <SelectItem value="Administrator">管理员</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="createOpen = false">取消</Button>
          <Button :disabled="creating || !botName.trim()" @click="createBot">
            <Spinner v-if="creating" data-icon="inline-start" />
            创建
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="issueOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>签发访问令牌</DialogTitle>
          <DialogDescription>为「{{ issueTarget?.userName }}」签发 Bot 访问令牌。</DialogDescription>
        </DialogHeader>
        <template v-if="!issuedToken">
          <FieldGroup>
            <Field>
              <FieldLabel for="token-ttl">有效期(秒)</FieldLabel>
              <Input id="token-ttl" v-model.number="expiresInSeconds" type="number" min="60" step="60" />
              <FieldDescription>默认 3600 秒(1 小时)。</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="issueOpen = false">取消</Button>
            <Button :disabled="issuing || !expiresInSeconds" @click="issueToken">
              <Spinner v-if="issuing" data-icon="inline-start" />
              签发
            </Button>
          </DialogFooter>
        </template>
        <template v-else>
          <Alert>
            <AlertDescription>令牌仅此一次展示,请立即复制保存,关闭后无法再次查看。</AlertDescription>
          </Alert>
          <FieldGroup>
            <Field>
              <FieldLabel for="issued-token">访问令牌</FieldLabel>
              <div class="flex items-center gap-2">
                <Input id="issued-token" :model-value="issuedToken.accessToken" readonly class="font-mono text-xs" />
                <Button size="icon" variant="outline" aria-label="复制令牌" @click="copyToken">
                  <Copy />
                </Button>
              </div>
              <FieldDescription>
                过期时间:<AdminDateTime :value="issuedToken.expiresAt" />
              </FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button @click="issueOpen = false">我已保存,关闭</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
  </div>
</template>
