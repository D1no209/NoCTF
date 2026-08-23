<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminChallengeConfigurationGet,
  adminChallengeConfigurationUpdate,
  adminCompetitionConfigurationGet,
  adminCreateCompetitionChallengeFlag,
  adminCreateCompetitionChallengeHint,
  adminDeleteCompetitionChallengeFlag,
  adminDeleteCompetitionChallengeHint,
  adminGetCompetitionChallenge,
  adminListCompetitionChallengeFlags,
  adminListCompetitionChallengeHints,
  adminRestoreCompetitionChallengeFlag,
  adminRestoreCompetitionChallengeHint,
  adminUpdateCompetitionChallenge,
  adminUpdateCompetitionChallengeFlag,
  adminUpdateCompetitionChallengeHint,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol,
  NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse,
  NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import type { GameModeValue } from '~/utils/game-config'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const ccId = route.params.ccId as string
const { competitionId, competition, canWrite } = useCompetitionAdmin()
const isAwdp = computed(() => competition.value?.mode === 'Awdp')

// ---- Challenge detail ----
const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

async function loadChallenge() {
  loading.value = true
  const { data, error } = await adminGetCompetitionChallenge({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: false },
  })
  if (error) loadError.value = parseApiError(error).message
  else challenge.value = data ?? null
  loading.value = false
}

// ---- Edit form ----
const editBaseScore = ref(0)
const editCustomTitle = ref('')
const editOrder = ref(0)
const editPublished = ref(false)
const savingEdit = ref(false)

watch(challenge, (c) => {
  if (!c) return
  editCustomTitle.value = c.customTitle ?? ''
  editBaseScore.value = c.baseScore ?? 0
  editOrder.value = c.order ?? 0
  editPublished.value = c.isPublished ?? false
}, { immediate: true })

async function saveEdit() {
  if (!challenge.value) return
  savingEdit.value = true
  try {
    const { data, error } = await adminUpdateCompetitionChallenge({
      path: { competitionId, competitionChallengeId: ccId },
      body: {
        customTitle: editCustomTitle.value.trim() || null,
        baseScore: isAwdp.value ? 0 : editBaseScore.value,
        order: editOrder.value,
        isPublished: editPublished.value,
      },
    })
    if (error) throw error
    challenge.value = data ?? challenge.value
    toast.success(translate("题目设置已保存"))
  }
  catch (e) {
    toastWriteError(e)
  }
  finally {
    savingEdit.value = false
  }
}

// ---- Per-challenge configuration ----
const config = ref<NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse | null>(null)
const configLoading = ref(true)
const savingConfig = ref(false)
const inheritedConfigJson = ref<string | null>(null)

async function loadConfig() {
  configLoading.value = true
  const [challengeResult, competitionResult] = await Promise.all([
    adminChallengeConfigurationGet({
      path: { competitionId, competitionChallengeId: ccId },
    }),
    adminCompetitionConfigurationGet({ path: { competitionId } }),
  ])
  if (!challengeResult.error && challengeResult.data) config.value = challengeResult.data
  if (!competitionResult.error && competitionResult.data) {
    inheritedConfigJson.value = competitionResult.data.json ?? null
  }
  configLoading.value = false
}

async function saveConfig(json: string) {
  if (!config.value) return
  savingConfig.value = true
  try {
    const { data, error } = await adminChallengeConfigurationUpdate({
      path: { competitionId, competitionChallengeId: ccId },
      body: { json },
    })
    if (error) throw error
    config.value = data ?? config.value
    toast.success(translate("题目配置已保存"))
  }
  catch (e) {
    toastWriteError(e)
  }
  finally {
    savingConfig.value = false
  }
}

// ---- Flags ----
const flags = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse[]>([])
const supportsRegularExpression = ref(false)
const flagsLoading = ref(true)
const flagsLoadError = ref<string | null>(null)
const includeDeletedFlags = ref(false)
const flagDialogOpen = ref(false)
const editingFlag = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse | null>(null)
const flagForm = ref({
  flag: '',
  matchKind: 'Exact' as NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol,
})
const flagError = ref<string | null>(null)
const savingFlag = ref(false)
const pendingFlagId = ref<string | null>(null)
const usesRuntimeFlagInjection = computed(() =>
  competition.value?.mode === 'Awdp'
  || competition.value?.mode === 'Ctf'
  && !flagsLoading.value
  && !flagsLoadError.value
  && !supportsRegularExpression.value,
)
const staticFlags = computed(() => flags.value.filter(flag => !flag.systemManaged))
const systemFlags = computed(() => flags.value.filter(flag => flag.systemManaged))

function challengeFlagErrorMessage(error: unknown): string {
  const failure = error as NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse
  switch (failure.code) {
    case 'InvalidFlag': return translate('Flag 内容无效')
    case 'InvalidRegularExpression': return translate('Flag 正则表达式无效')
    case 'RegularExpressionNotSupported': return translate('当前题目不支持正则 Flag')
    case 'ManualFlagNotSupported': return translate('动态容器 Flag 由平台自动生成和注入')
    case 'SystemManagedFlag': return translate('系统生成的动态 Flag 只读，不能手工修改')
    case 'DeliveryModeConflict': return translate('普通附件或静态 Flag 不能与每队随机附件混用')
    case 'ResourceIdConflict': return translate('Flag 资源标识冲突，请重新添加')
    default: return parseApiError(error).message
  }
}

async function loadFlags() {
  flagsLoading.value = true
  const { data, error } = await adminListCompetitionChallengeFlags({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: includeDeletedFlags.value },
  })
  if (error || !data) {
    flagsLoadError.value = parseApiError(error).message
    supportsRegularExpression.value = false
  }
  else {
    flagsLoadError.value = null
    flags.value = data.items ?? []
    supportsRegularExpression.value = data.supportsRegularExpression ?? false
    if (!supportsRegularExpression.value && flagForm.value.matchKind === 'RegularExpression')
      flagForm.value.matchKind = 'Exact'
  }
  flagsLoading.value = false
}
watch(includeDeletedFlags, loadFlags)

function openFlagDialog(flag?: NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse) {
  editingFlag.value = flag ?? null
  flagError.value = null
  flagForm.value = {
    flag: flag?.flag ?? '',
    matchKind: flag?.matchKind ?? 'Exact',
  }
  flagDialogOpen.value = true
}

async function saveFlag() {
  if (!flagForm.value.flag.trim()) {
    flagError.value = translate('请输入 Flag 内容')
    return
  }
  savingFlag.value = true
  flagError.value = null
  const body = {
    flag: flagForm.value.flag,
    matchKind: flagForm.value.matchKind,
  }
  try {
    const path = { competitionId, competitionChallengeId: ccId }
    const { error } = editingFlag.value?.id
      ? await adminUpdateCompetitionChallengeFlag({ path: { ...path, flagId: editingFlag.value.id }, body })
      : await adminCreateCompetitionChallengeFlag({ path, body })
    if (error) throw error
    toast.success(editingFlag.value ? translate("Flag 已更新") : translate("Flag 已添加"))
    flagDialogOpen.value = false
    await loadFlags()
  }
  catch (e) {
    flagError.value = challengeFlagErrorMessage(e)
  }
  finally {
    savingFlag.value = false
  }
}

async function deleteFlag(f: NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse) {
  if (!f.id) return
  pendingFlagId.value = f.id
  try {
    const { error } = await adminDeleteCompetitionChallengeFlag({
      path: { competitionId, competitionChallengeId: ccId, flagId: f.id },
    })
    if (error) throw error
    toast.success(translate("Flag 已删除"))
    await loadFlags()
  }
  catch (e) {
    toast.error(challengeFlagErrorMessage(e))
  }
  finally {
    pendingFlagId.value = null
  }
}

async function restoreFlag(f: NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse) {
  if (!f.id) return
  pendingFlagId.value = f.id
  try {
    const { error } = await adminRestoreCompetitionChallengeFlag({
      path: { competitionId, competitionChallengeId: ccId, flagId: f.id },
    })
    if (error) throw error
    toast.success(translate("Flag 已恢复"))
    await loadFlags()
  }
  catch (e) {
    toast.error(challengeFlagErrorMessage(e))
  }
  finally {
    pendingFlagId.value = null
  }
}

// ---- Hints ----
const hints = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse[]>([])
const hintsLoading = ref(true)
const hintsLoadError = ref<string | null>(null)
const includeDeletedHints = ref(false)
const hintDialogOpen = ref(false)
const editingHint = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse | null>(null)
const hintForm = ref({ content: '', cost: 0, publishedAt: '' })
const hintError = ref<string | null>(null)
const savingHint = ref(false)
const pendingHintId = ref<string | null>(null)

async function loadHints() {
  hintsLoading.value = true
  const { data, error } = await adminListCompetitionChallengeHints({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: includeDeletedHints.value },
  })
  if (error || !data) {
    hintsLoadError.value = parseApiError(error).message
  }
  else {
    hintsLoadError.value = null
    hints.value = data.items ?? []
  }
  hintsLoading.value = false
}
watch(includeDeletedHints, loadHints)

function openHintDialog(hint?: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  editingHint.value = hint ?? null
  hintError.value = null
  hintForm.value = {
    content: hint?.content ?? '',
    cost: hint?.cost ?? 0,
    publishedAt: isoToLocalInput(hint?.publishedAt),
  }
  hintDialogOpen.value = true
}

async function saveHint() {
  if (!hintForm.value.content.trim()) {
    hintError.value = translate('请输入提示内容')
    return
  }
  savingHint.value = true
  hintError.value = null
  const body = {
    content: hintForm.value.content,
    cost: hintForm.value.cost,
    publishedAt: localInputToIso(hintForm.value.publishedAt) ?? null,
  }
  try {
    const path = { competitionId, competitionChallengeId: ccId }
    const { error } = editingHint.value?.id
      ? await adminUpdateCompetitionChallengeHint({ path: { ...path, hintId: editingHint.value.id }, body })
      : await adminCreateCompetitionChallengeHint({ path, body })
    if (error) throw error
    toast.success(editingHint.value ? translate("提示已更新") : translate("提示已添加"))
    hintDialogOpen.value = false
    await loadHints()
  }
  catch (e) {
    hintError.value = parseApiError(e).message
  }
  finally {
    savingHint.value = false
  }
}

async function deleteHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  if (!h.id) return
  pendingHintId.value = h.id
  try {
    const { error } = await adminDeleteCompetitionChallengeHint({
      path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
    })
    if (error) throw error
    toast.success(translate("提示已删除"))
    await loadHints()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingHintId.value = null
  }
}

async function restoreHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  if (!h.id) return
  pendingHintId.value = h.id
  try {
    const { error } = await adminRestoreCompetitionChallengeHint({
      path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
    })
    if (error) throw error
    toast.success(translate("提示已恢复"))
    await loadHints()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingHintId.value = null
  }
}

onMounted(() => {
  void loadChallenge()
  void loadConfig()
  void loadFlags()
  void loadHints()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <Button variant="ghost" size="sm" as-child class="w-fit">
      <NuxtLink :to="`/admin/competitions/${competitionId}/challenges`">{{ $t('← 返回题目列表') }}</NuxtLink>
    </Button>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>
    <Skeleton v-else-if="loading" class="h-32 w-full" />

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-display text-xl">{{ challenge.title }}</h2>
        <Badge :variant="challenge.isPublished ? 'default' : 'outline'">
          {{ challenge.isPublished ? $t('已发布') : $t('未发布') }}
        </Badge>
        <Badge variant="secondary">{{ challenge.direction }}</Badge>
      </div>

      <Tabs default-value="general">
        <TabsList>
          <TabsTrigger value="general">{{ $t('基本设置') }}</TabsTrigger>
          <TabsTrigger value="config">{{ $t('题目配置') }}</TabsTrigger>
          <TabsTrigger value="flags">Flags</TabsTrigger>
          <TabsTrigger value="hints">{{ $t('提示') }}</TabsTrigger>
        </TabsList>

        <TabsContent value="general" class="mt-4">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('基本设置') }}</CardTitle>
            </CardHeader>
            <CardContent>
              <form @submit.prevent="saveEdit">
                <FieldGroup>
                  <Field>
                    <FieldLabel for="cc-title">{{ $t('比赛题目名称') }}</FieldLabel>
                    <Input
                      id="cc-title"
                      v-model="editCustomTitle"
                      maxlength="160"
                      :readonly="!canWrite"
                      :placeholder="$t('留空时使用题库模板标题')"
                    />
                    <FieldDescription>{{ $t('只修改本场比赛中的展示名称,不会更改题库模板') }}</FieldDescription>
                  </Field>
                  <div class="grid gap-4 sm:grid-cols-2">
                    <Field v-if="!isAwdp">
                      <FieldLabel for="cc-score">{{ $t('基础分') }}</FieldLabel>
                      <Input id="cc-score" v-model.number="editBaseScore" type="number" min="0" :readonly="!canWrite" />
                    </Field>
                    <Field>
                      <FieldLabel for="cc-order">{{ $t('顺序') }}</FieldLabel>
                      <Input id="cc-order" v-model.number="editOrder" type="number" min="0" :readonly="!canWrite" />
                    </Field>
                  </div>
                  <Field orientation="horizontal">
                    <Switch id="cc-published" v-model="editPublished" :disabled="!canWrite" />
                    <FieldLabel for="cc-published" class="font-normal">{{ $t('发布该题目(对选手可见)') }}</FieldLabel>
                  </Field>
                  <Field v-if="canWrite">
                    <Button type="submit" :disabled="savingEdit">
                      <Spinner v-if="savingEdit" data-icon="inline-start" /> {{ $t('保存设置') }} </Button>
                  </Field>
                </FieldGroup>
              </form>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="config" class="mt-4">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('题目规则') }}</CardTitle>
              <CardDescription>{{ $t('该题目在本竞赛中的模式专属规则;未覆盖的字段继承竞赛默认') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <ChallengeRulesEditor
                :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
                :json="config?.json"
                :inherited-json="inheritedConfigJson"
                :readonly="!canWrite"
                :loading="configLoading"
                :saving="savingConfig"
                @save="saveConfig"
              />
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="flags" class="mt-4">
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <Checkbox id="show-deleted-flags" v-model="includeDeletedFlags" />
                <label for="show-deleted-flags" class="text-sm text-muted-foreground">{{ $t('显示已删除') }}</label>
              </div>
              <Button v-if="canWrite && !usesRuntimeFlagInjection" size="sm" @click="openFlagDialog()">
                <Plus data-icon="inline-start" /> {{ $t('添加 Flag') }} </Button>
            </div>
            <Alert v-if="usesRuntimeFlagInjection">
              <AlertDescription>
                {{ $t('该题使用运行环境动态 Flag。平台会为每支队伍生成 Flag，并在启动容器时注入环境变量；无需维护精确或正则 Flag。') }}
              </AlertDescription>
            </Alert>
            <Alert v-if="flagsLoadError" variant="destructive">
              <AlertDescription>{{ flagsLoadError }}</AlertDescription>
            </Alert>
            <Skeleton v-if="flagsLoading" class="h-32 w-full" />
            <Empty v-else-if="!flagsLoadError && staticFlags.length === 0 && systemFlags.length === 0" class="border border-dashed py-12">
              <EmptyHeader>
                <EmptyTitle>{{ $t('暂无 Flag') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-if="!flagsLoading && staticFlags.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>Flag</TableHead>
                  <TableHead>{{ $t('状态') }}</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">{{ $t('操作') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="f in staticFlags" :key="f.id" :class="{ 'opacity-60': f.deletedAt }">
                  <TableCell class="max-w-64">
                    <div class="flex min-w-0 items-center gap-2">
                      <Badge variant="outline">
                        {{ f.matchKind === 'RegularExpression' ? $t('正则匹配') : $t('精确匹配') }}
                      </Badge>
                      <span class="truncate font-mono text-xs">{{ f.flag }}</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge v-if="f.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                    <Badge v-else variant="secondary">{{ $t('有效') }}</Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!f.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingFlagId === f.id" @click="openFlagDialog(f)">{{ $t('编辑') }}</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingFlagId === f.id" @click="deleteFlag(f)">{{ $t('删除') }}</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingFlagId === f.id" @click="restoreFlag(f)">{{ $t('恢复') }}</Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <div v-if="!flagsLoading && systemFlags.length > 0" class="space-y-2">
              <div>
                <h3 class="text-sm font-semibold">{{ $t('系统生成的动态 Flag') }}</h3>
                <p class="text-sm text-muted-foreground">{{ $t('由平台自动关联附件、队伍、轮次或运行环境，仅供查看。') }}</p>
              </div>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Flag</TableHead>
                    <TableHead>{{ $t('队伍') }}</TableHead>
                    <TableHead>{{ $t('内部关联') }}</TableHead>
                    <TableHead>{{ $t('有效期') }}</TableHead>
                    <TableHead>{{ $t('状态') }}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="f in systemFlags" :key="f.id" :class="{ 'opacity-60': f.deletedAt }">
                    <TableCell class="max-w-64 truncate font-mono text-xs" :title="f.flag">{{ f.flag }}</TableCell>
                    <TableCell class="font-mono text-xs">{{ f.teamId ?? '—' }}</TableCell>
                    <TableCell class="font-mono text-xs">
                      {{ f.specificationKind ?? '—' }}<span v-if="f.specificationId"> · {{ f.specificationId }}</span>
                    </TableCell>
                    <TableCell class="font-mono text-xs tabular-nums">
                      {{ adminFormatDateTime(f.validStart) }} ~ {{ adminFormatDateTime(f.validUntil) }}
                    </TableCell>
                    <TableCell>
                      <Badge v-if="f.deletedAt" variant="destructive">{{ $t('已失效') }}</Badge>
                      <Badge v-else variant="secondary">{{ $t('已注入') }}</Badge>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </div>
        </TabsContent>

        <TabsContent value="hints" class="mt-4">
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <Checkbox id="show-deleted-hints" v-model="includeDeletedHints" />
                <label for="show-deleted-hints" class="text-sm text-muted-foreground">{{ $t('显示已删除') }}</label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openHintDialog()">
                <Plus data-icon="inline-start" /> {{ $t('添加提示') }} </Button>
            </div>
            <Alert v-if="hintsLoadError" variant="destructive">
              <AlertDescription>{{ hintsLoadError }}</AlertDescription>
            </Alert>
            <Skeleton v-if="hintsLoading" class="h-32 w-full" />
            <Empty v-else-if="!hintsLoadError && hints.length === 0" class="border border-dashed py-12">
              <EmptyHeader>
                <EmptyTitle>{{ $t('暂无提示') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else-if="hints.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('内容') }}</TableHead>
                  <TableHead class="w-20">{{ $t('扣分') }}</TableHead>
                  <TableHead class="w-44">{{ $t('发布时间') }}</TableHead>
                  <TableHead class="w-24">{{ $t('状态') }}</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">{{ $t('操作') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="h in hints" :key="h.id" :class="{ 'opacity-60': h.deletedAt }">
                  <TableCell class="max-w-96 truncate">{{ h.content }}</TableCell>
                  <TableCell class="font-mono tabular-nums">{{ h.cost }}</TableCell>
                  <TableCell class="font-mono text-xs tabular-nums">{{ h.publishedAt ? adminFormatDateTime(h.publishedAt) : $t('未发布') }}</TableCell>
                  <TableCell>
                    <Badge v-if="h.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                    <Badge v-else :variant="h.publishedAt ? 'default' : 'outline'">
                      {{ h.publishedAt ? $t('已发布') : $t('未发布') }}
                    </Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!h.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="openHintDialog(h)">{{ $t('编辑') }}</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="deleteHint(h)">{{ $t('删除') }}</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingHintId === h.id" @click="restoreHint(h)">{{ $t('恢复') }}</Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </TabsContent>
      </Tabs>

      <Dialog v-model:open="flagDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingFlag ? $t('编辑 Flag') : $t('添加 Flag') }}</DialogTitle>
            <DialogDescription>{{ $t('普通静态 Flag 只需要选择匹配方式并填写内容。') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="flagError" variant="destructive">
              <AlertDescription>{{ flagError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="flag-match-kind">{{ $t('匹配方式') }}</FieldLabel>
              <Select v-model="flagForm.matchKind">
                <SelectTrigger id="flag-match-kind" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Exact">{{ $t('精确匹配') }}</SelectItem>
                  <SelectItem v-if="supportsRegularExpression" value="RegularExpression">
                    {{ $t('正则匹配') }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FieldDescription>
                {{ $t('正则表达式匹配完整 Flag，区分大小写；仅 CTF 静态 Flag 可用。') }}
              </FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="flag-value">
                {{ flagForm.matchKind === 'RegularExpression' ? $t('正则表达式') : $t('Flag 内容') }}
              </FieldLabel>
              <Input
                id="flag-value"
                v-model="flagForm.flag"
                class="font-mono"
                required
                :placeholder="flagForm.matchKind === 'RegularExpression' ? 'flag\\{[0-9a-f-]{36}\\}' : 'flag{...}'"
              />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="flagDialogOpen = false">{{ $t('取消') }}</Button>
            <Button :disabled="savingFlag" @click="saveFlag">
              <Spinner v-if="savingFlag" data-icon="inline-start" /> {{ $t('保存') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="hintDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingHint ? $t('编辑提示') : $t('添加提示') }}</DialogTitle>
            <DialogDescription>{{ $t('发布时间留空表示暂不发布') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="hintError" variant="destructive">
              <AlertDescription>{{ hintError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="hint-content">{{ $t('提示内容') }}</FieldLabel>
              <Textarea id="hint-content" v-model="hintForm.content" required />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="hint-cost">{{ $t('扣分') }}</FieldLabel>
                <Input id="hint-cost" v-model.number="hintForm.cost" type="number" min="0" />
              </Field>
              <Field>
                <FieldLabel for="hint-publish">{{ $t('发布时间(可选)') }}</FieldLabel>
                <Input id="hint-publish" v-model="hintForm.publishedAt" type="datetime-local" />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="hintDialogOpen = false">{{ $t('取消') }}</Button>
            <Button :disabled="savingHint" @click="saveHint">
              <Spinner v-if="savingHint" data-icon="inline-start" /> {{ $t('保存') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
  </div>
</template>
