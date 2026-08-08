<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminChallengeConfigurationGet,
  adminChallengeConfigurationUpdate,
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
  NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse,
  NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsAdministrationChallengeBankSpecificationKindProtocol,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import type { GameModeValue } from '~/utils/game-config'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const ccId = route.params.ccId as string
const { competitionId, competition, canWrite } = useCompetitionAdmin()

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
const editOrder = ref(0)
const editPublished = ref(false)
const savingEdit = ref(false)

watch(challenge, (c) => {
  if (!c) return
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
        baseScore: editBaseScore.value,
        order: editOrder.value,
        isPublished: editPublished.value,
        expectedRevision: challenge.value.revision ?? 0,
      },
    })
    if (error) throw error
    challenge.value = data ?? challenge.value
    toast.success('题目设置已保存')
  }
  catch (e) {
    toastWriteError(e, loadChallenge)
  }
  finally {
    savingEdit.value = false
  }
}

// ---- Per-challenge configuration ----
const config = ref<NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse | null>(null)
const configLoading = ref(true)
const savingConfig = ref(false)

async function loadConfig() {
  configLoading.value = true
  const { data, error } = await adminChallengeConfigurationGet({
    path: { competitionId, competitionChallengeId: ccId },
  })
  if (!error && data) config.value = data
  configLoading.value = false
}

async function saveConfig(json: string) {
  if (!config.value) return
  savingConfig.value = true
  try {
    const { data, error } = await adminChallengeConfigurationUpdate({
      path: { competitionId, competitionChallengeId: ccId },
      body: { json, expectedRevision: config.value.revision ?? 0 },
    })
    if (error) throw error
    config.value = data ?? config.value
    toast.success('题目配置已保存')
  }
  catch (e) {
    toastWriteError(e, loadConfig)
  }
  finally {
    savingConfig.value = false
  }
}

// ---- Flags ----
const flags = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse[]>([])
const flagsLoading = ref(true)
const includeDeletedFlags = ref(false)
const flagDialogOpen = ref(false)
const editingFlag = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse | null>(null)
const flagForm = ref({ flag: '', teamId: '', specificationKind: '', specificationId: '', validStart: '', validUntil: '' })
const flagError = ref<string | null>(null)
const savingFlag = ref(false)
const pendingFlagId = ref<string | null>(null)

async function loadFlags() {
  flagsLoading.value = true
  const { data, error } = await adminListCompetitionChallengeFlags({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: includeDeletedFlags.value },
  })
  if (!error) flags.value = data?.items ?? []
  flagsLoading.value = false
}
watch(includeDeletedFlags, loadFlags)

function openFlagDialog(flag?: NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse) {
  editingFlag.value = flag ?? null
  flagError.value = null
  flagForm.value = {
    flag: flag?.flag ?? '',
    teamId: flag?.teamId ?? '',
    specificationKind: flag?.specificationKind !== null && flag?.specificationKind !== undefined ? String(flag.specificationKind) : '',
    specificationId: flag?.specificationId ?? '',
    validStart: isoToLocalInput(flag?.validStart),
    validUntil: isoToLocalInput(flag?.validUntil),
  }
  flagDialogOpen.value = true
}

async function saveFlag() {
  if (!flagForm.value.flag.trim()) {
    flagError.value = '请输入 Flag 内容'
    return
  }
  savingFlag.value = true
  flagError.value = null
  const body = {
    flag: flagForm.value.flag,
    teamId: flagForm.value.teamId.trim() || null,
    specificationKind: flagForm.value.specificationKind === ''
      ? null
      : flagForm.value.specificationKind as NoCtfapiEndpointsAdministrationChallengeBankSpecificationKindProtocol,
    specificationId: flagForm.value.specificationId.trim() || null,
    validStart: localInputToIso(flagForm.value.validStart) ?? null,
    validUntil: localInputToIso(flagForm.value.validUntil) ?? null,
  }
  try {
    const path = { competitionId, competitionChallengeId: ccId }
    const { error } = editingFlag.value?.id
      ? await adminUpdateCompetitionChallengeFlag({ path: { ...path, flagId: editingFlag.value.id }, body })
      : await adminCreateCompetitionChallengeFlag({ path, body })
    if (error) throw error
    toast.success(editingFlag.value ? 'Flag 已更新' : 'Flag 已添加')
    flagDialogOpen.value = false
    await loadFlags()
  }
  catch (e) {
    flagError.value = parseApiError(e).message
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
    toast.success('Flag 已删除')
    await loadFlags()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
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
    toast.success('Flag 已恢复')
    await loadFlags()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingFlagId.value = null
  }
}

// ---- Hints ----
const hints = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse[]>([])
const hintsLoading = ref(true)
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
  if (!error) hints.value = data?.items ?? []
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
    hintError.value = '请输入提示内容'
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
    toast.success(editingHint.value ? '提示已更新' : '提示已添加')
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
    toast.success('提示已删除')
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
    toast.success('提示已恢复')
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
      <NuxtLink :to="`/admin/competitions/${competitionId}/challenges`">← 返回题目列表</NuxtLink>
    </Button>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>
    <Skeleton v-else-if="loading" class="h-32 w-full" />

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-xl font-semibold">{{ challenge.title }}</h2>
        <Badge :variant="challenge.isPublished ? 'default' : 'outline'">
          {{ challenge.isPublished ? '已发布' : '未发布' }}
        </Badge>
        <Badge variant="secondary">{{ challenge.direction }}</Badge>
      </div>

      <Tabs default-value="general">
        <TabsList>
          <TabsTrigger value="general">基本设置</TabsTrigger>
          <TabsTrigger value="config">题目配置</TabsTrigger>
          <TabsTrigger value="flags">Flags</TabsTrigger>
          <TabsTrigger value="hints">提示</TabsTrigger>
        </TabsList>

        <TabsContent value="general" class="mt-4">
          <Card>
            <CardHeader>
              <CardTitle>基本设置</CardTitle>
              <CardDescription>修订版本:{{ challenge.revision }}</CardDescription>
            </CardHeader>
            <CardContent>
              <form @submit.prevent="saveEdit">
                <FieldGroup>
                  <div class="grid gap-4 sm:grid-cols-2">
                    <Field>
                      <FieldLabel for="cc-score">基础分</FieldLabel>
                      <Input id="cc-score" v-model.number="editBaseScore" type="number" min="0" :readonly="!canWrite" />
                    </Field>
                    <Field>
                      <FieldLabel for="cc-order">顺序</FieldLabel>
                      <Input id="cc-order" v-model.number="editOrder" type="number" min="0" :readonly="!canWrite" />
                    </Field>
                  </div>
                  <Field orientation="horizontal">
                    <Switch id="cc-published" v-model="editPublished" :disabled="!canWrite" />
                    <FieldLabel for="cc-published" class="font-normal">发布该题目(对选手可见)</FieldLabel>
                  </Field>
                  <Field v-if="canWrite">
                    <Button type="submit" :disabled="savingEdit">
                      <Spinner v-if="savingEdit" data-icon="inline-start" />
                      保存设置
                    </Button>
                  </Field>
                </FieldGroup>
              </form>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="config" class="mt-4">
          <Card>
            <CardHeader>
              <CardTitle>题目规则</CardTitle>
              <CardDescription>该题目在本竞赛中的模式专属规则;未覆盖的字段继承竞赛默认</CardDescription>
            </CardHeader>
            <CardContent>
              <ChallengeRulesEditor
                :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
                :json="config?.json"
                :revision="config?.revision"
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
                <label for="show-deleted-flags" class="text-sm text-muted-foreground">显示已删除</label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openFlagDialog()">
                <Plus data-icon="inline-start" />
                添加 Flag
              </Button>
            </div>
            <Skeleton v-if="flagsLoading" class="h-32 w-full" />
            <Empty v-else-if="flags.length === 0">
              <EmptyHeader>
                <EmptyTitle>暂无 Flag</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else>
              <TableHeader>
                <TableRow>
                  <TableHead>Flag</TableHead>
                  <TableHead>队伍</TableHead>
                  <TableHead>规格</TableHead>
                  <TableHead>有效期</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="f in flags" :key="f.id" :class="{ 'opacity-60': f.deletedAt }">
                  <TableCell class="max-w-64 truncate font-mono text-xs">{{ f.flag }}</TableCell>
                  <TableCell class="font-mono text-xs">{{ f.teamId ?? '全部' }}</TableCell>
                  <TableCell>
                    <span v-if="f.specificationKind !== null && f.specificationKind !== undefined">
                      {{ enumLabel(SpecificationKindLabel, f.specificationKind) }}
                    </span>
                    <span v-else>—</span>
                  </TableCell>
                  <TableCell class="text-xs">
                    {{ adminFormatDateTime(f.validStart) }} ~ {{ adminFormatDateTime(f.validUntil) }}
                  </TableCell>
                  <TableCell>
                    <Badge v-if="f.deletedAt" variant="destructive">已删除</Badge>
                    <Badge v-else variant="secondary">有效</Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!f.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingFlagId === f.id" @click="openFlagDialog(f)">编辑</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingFlagId === f.id" @click="deleteFlag(f)">删除</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingFlagId === f.id" @click="restoreFlag(f)">恢复</Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </TabsContent>

        <TabsContent value="hints" class="mt-4">
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <Checkbox id="show-deleted-hints" v-model="includeDeletedHints" />
                <label for="show-deleted-hints" class="text-sm text-muted-foreground">显示已删除</label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openHintDialog()">
                <Plus data-icon="inline-start" />
                添加提示
              </Button>
            </div>
            <Skeleton v-if="hintsLoading" class="h-32 w-full" />
            <Empty v-else-if="hints.length === 0">
              <EmptyHeader>
                <EmptyTitle>暂无提示</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else>
              <TableHeader>
                <TableRow>
                  <TableHead>内容</TableHead>
                  <TableHead class="w-20">扣分</TableHead>
                  <TableHead class="w-44">发布时间</TableHead>
                  <TableHead class="w-24">状态</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="h in hints" :key="h.id" :class="{ 'opacity-60': h.deletedAt }">
                  <TableCell class="max-w-96 truncate">{{ h.content }}</TableCell>
                  <TableCell>{{ h.cost }}</TableCell>
                  <TableCell>{{ h.publishedAt ? adminFormatDateTime(h.publishedAt) : '未发布' }}</TableCell>
                  <TableCell>
                    <Badge v-if="h.deletedAt" variant="destructive">已删除</Badge>
                    <Badge v-else :variant="h.publishedAt ? 'default' : 'outline'">
                      {{ h.publishedAt ? '已发布' : '未发布' }}
                    </Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!h.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="openHintDialog(h)">编辑</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="deleteHint(h)">删除</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingHintId === h.id" @click="restoreHint(h)">恢复</Button>
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
            <DialogTitle>{{ editingFlag ? '编辑 Flag' : '添加 Flag' }}</DialogTitle>
            <DialogDescription>队伍留空表示适用于全部队伍的静态 Flag</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="flagError" variant="destructive">
              <AlertDescription>{{ flagError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="flag-value">Flag 内容</FieldLabel>
              <Input id="flag-value" v-model="flagForm.flag" class="font-mono" required />
            </Field>
            <Field>
              <FieldLabel for="flag-team">队伍 ID(可选)</FieldLabel>
              <Input id="flag-team" v-model="flagForm.teamId" class="font-mono" placeholder="留空 = 全部队伍" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="flag-spec-kind">规格类型(可选)</FieldLabel>
                <Select id="flag-spec-kind" v-model="flagForm.specificationKind">
                  <SelectTrigger class="w-full">
                    <SelectValue placeholder="无" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="Attachment">附件</SelectItem>
                      <SelectItem value="AwdRound">AWD 轮次</SelectItem>
                      <SelectItem value="RuntimeDefinition">运行时定义</SelectItem>
                      <SelectItem value="Hint">提示</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="flag-spec-id">规格 ID(可选)</FieldLabel>
                <Input id="flag-spec-id" v-model="flagForm.specificationId" class="font-mono" />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="flag-valid-start">生效时间(可选)</FieldLabel>
                <Input id="flag-valid-start" v-model="flagForm.validStart" type="datetime-local" />
              </Field>
              <Field>
                <FieldLabel for="flag-valid-until">失效时间(可选)</FieldLabel>
                <Input id="flag-valid-until" v-model="flagForm.validUntil" type="datetime-local" />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="flagDialogOpen = false">取消</Button>
            <Button :disabled="savingFlag" @click="saveFlag">
              <Spinner v-if="savingFlag" data-icon="inline-start" />
              保存
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="hintDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingHint ? '编辑提示' : '添加提示' }}</DialogTitle>
            <DialogDescription>发布时间留空表示暂不发布</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="hintError" variant="destructive">
              <AlertDescription>{{ hintError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="hint-content">提示内容</FieldLabel>
              <Textarea id="hint-content" v-model="hintForm.content" required />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="hint-cost">扣分</FieldLabel>
                <Input id="hint-cost" v-model.number="hintForm.cost" type="number" min="0" />
              </Field>
              <Field>
                <FieldLabel for="hint-publish">发布时间(可选)</FieldLabel>
                <Input id="hint-publish" v-model="hintForm.publishedAt" type="datetime-local" />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="hintDialogOpen = false">取消</Button>
            <Button :disabled="savingHint" @click="saveHint">
              <Spinner v-if="savingHint" data-icon="inline-start" />
              保存
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
  </div>
</template>
