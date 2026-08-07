<script setup lang="ts">
import { Paperclip, RotateCcw, Trash2, Upload } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminChallengeBankCreateFlag,
  adminChallengeBankDeleteAttachment,
  adminChallengeBankDeleteFlag,
  adminChallengeBankDeleteTemplate,
  adminChallengeBankGetTemplate,
  adminChallengeBankListAttachments,
  adminChallengeBankListFlags,
  adminChallengeBankRestoreAttachment,
  adminChallengeBankRestoreFlag,
  adminChallengeBankRestoreTemplate,
  adminChallengeBankTransferOwner,
  adminChallengeBankUpdateAttachment,
  adminChallengeBankUpdatePermissions,
  adminChallengeBankUpdateTemplate,
  adminChallengeBankUploadAttachment,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  NoCtfDomainChallengesChallengeVisibility2,
  NoCtfDomainCompetitionsGameMode2,
} from '~/api'

definePageMeta({ middleware: 'auth' })

type Template = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse
type Attachment = NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse
type Flag = NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse

const route = useRoute()
const challengeId = route.params.id as string
const { canOrganize } = useAuth()

const template = ref<Template | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

const MODE_NAMES: NoCtfDomainCompetitionsGameMode2[] = ['Ctf', 'Awd', 'Awdp', 'Koh']

// ---------- 基本信息 ----------
const form = reactive({
  title: '',
  mode: 'Ctf' as NoCtfDomainCompetitionsGameMode2,
  visibility: 'Private' as NoCtfDomainChallengesChallengeVisibility2,
  direction: '',
  description: '',
  definitionJson: '{}',
})
const saving = ref(false)
const deleting = ref(false)
const restoring = ref(false)

const definitionJsonError = computed(() => {
  try {
    JSON.parse(form.definitionJson)
    return null
  }
  catch {
    return '定义 JSON 格式错误'
  }
})

const isDeleted = computed(() => !!template.value?.deletedAt)

function syncForm(value: Template): void {
  form.title = value.title ?? ''
  form.mode = MODE_NAMES[value.mode ?? 0] ?? 'Ctf'
  form.visibility = value.visibility === 1 ? 'Shared' : 'Private'
  form.direction = value.direction ?? ''
  form.description = value.description ?? ''
  form.definitionJson = value.definitionJson ?? '{}'
}

async function loadTemplate(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminChallengeBankGetTemplate({
    path: { challengeId },
    query: { includeDeleted: true },
  })
  loading.value = false
  if (error || !data) {
    loadError.value = parseApiError(error, '模板不存在或加载失败').message
    return
  }
  template.value = data
  syncForm(data)
  syncPermissions(data)
}

async function save(): Promise<void> {
  if (!template.value || definitionJsonError.value) return
  saving.value = true
  const { data, error, response } = await adminChallengeBankUpdateTemplate({
    path: { challengeId },
    body: {
      title: form.title.trim(),
      mode: form.mode,
      visibility: form.visibility,
      direction: form.direction.trim(),
      description: form.description.trim() || null,
      definitionJson: form.definitionJson,
      expectedRevision: template.value.revision ?? 0,
    },
  })
  saving.value = false
  if (error) {
    if (response?.status === 409) {
      toast.error('模板已被他人修改,请刷新后重试')
      await loadTemplate()
    }
    else {
      toast.error(parseApiError(error).message)
    }
    return
  }
  if (data) {
    template.value = data
    syncForm(data)
  }
  toast.success('已保存')
}

async function removeTemplate(): Promise<void> {
  deleting.value = true
  const { error } = await adminChallengeBankDeleteTemplate({ path: { challengeId } })
  deleting.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success('模板已删除')
  await navigateTo('/admin/challenges')
}

async function restoreTemplate(): Promise<void> {
  restoring.value = true
  const { error } = await adminChallengeBankRestoreTemplate({ path: { challengeId } })
  restoring.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success('模板已恢复')
  await loadTemplate()
}

// ---------- 附件 ----------
const attachments = ref<Attachment[]>([])
const attachmentsLoading = ref(false)
const attachmentsIncludeDeleted = ref(false)
const uploading = ref(false)
const uploadInput = ref<HTMLInputElement | null>(null)
const editingAttachment = ref<Attachment | null>(null)
const attachmentEditForm = reactive({ fileName: '', contentType: '' })
const attachmentEditOpen = ref(false)
const attachmentSaving = ref(false)
const deletingAttachment = ref<Attachment | null>(null)
const attachmentActionPending = ref(false)

async function loadAttachments(): Promise<void> {
  attachmentsLoading.value = true
  const { data, error } = await adminChallengeBankListAttachments({
    path: { challengeId },
    query: { includeDeleted: attachmentsIncludeDeleted.value },
  })
  attachmentsLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  attachments.value = data?.items ?? []
}

watch(attachmentsIncludeDeleted, () => {
  void loadAttachments()
})

async function uploadAttachment(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  uploading.value = true
  const { error } = await adminChallengeBankUploadAttachment({
    path: { challengeId },
    body: { file },
  })
  uploading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success(`已上传 ${file.name}`)
  await loadAttachments()
}

function openAttachmentEdit(attachment: Attachment): void {
  editingAttachment.value = attachment
  attachmentEditForm.fileName = attachment.fileName ?? ''
  attachmentEditForm.contentType = attachment.contentType ?? ''
  attachmentEditOpen.value = true
}

async function saveAttachment(): Promise<void> {
  const attachment = editingAttachment.value
  if (!attachment?.id) return
  attachmentSaving.value = true
  const { error } = await adminChallengeBankUpdateAttachment({
    path: { challengeId, attachmentId: attachment.id },
    body: {
      fileName: attachmentEditForm.fileName.trim(),
      contentType: attachmentEditForm.contentType.trim(),
    },
  })
  attachmentSaving.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  attachmentEditOpen.value = false
  toast.success('附件已更新')
  await loadAttachments()
}

async function confirmDeleteAttachment(): Promise<void> {
  const attachment = deletingAttachment.value
  if (!attachment?.id) return
  attachmentActionPending.value = true
  const { error } = await adminChallengeBankDeleteAttachment({
    path: { challengeId, attachmentId: attachment.id },
  })
  attachmentActionPending.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  deletingAttachment.value = null
  toast.success('附件已删除')
  await loadAttachments()
}

async function restoreAttachment(attachment: Attachment): Promise<void> {
  if (!attachment.id) return
  attachmentActionPending.value = true
  const { error } = await adminChallengeBankRestoreAttachment({
    path: { challengeId, attachmentId: attachment.id },
  })
  attachmentActionPending.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success('附件已恢复')
  await loadAttachments()
}

function formatBytes(value?: number): string {
  if (value === undefined || value === null) return '—'
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / 1024 / 1024).toFixed(1)} MB`
}

// ---------- Flags ----------
const flags = ref<Flag[]>([])
const flagsLoading = ref(false)
const flagsIncludeDeleted = ref(false)
const flagCreateOpen = ref(false)
const flagCreating = ref(false)
const flagForm = reactive({
  flag: '',
  teamId: '',
  specificationKind: '',
  specificationId: '',
  validStart: '',
  validUntil: '',
})
const deletingFlag = ref<Flag | null>(null)
const flagActionPending = ref(false)

const SPECIFICATION_KINDS = [
  { value: '0', label: '附件' },
  { value: '1', label: 'AWD 轮次' },
  { value: '2', label: '运行时定义' },
  { value: '3', label: '提示' },
] as const

async function loadFlags(): Promise<void> {
  flagsLoading.value = true
  const { data, error } = await adminChallengeBankListFlags({
    path: { challengeId },
    query: { includeDeleted: flagsIncludeDeleted.value },
  })
  flagsLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  flags.value = data?.items ?? []
}

watch(flagsIncludeDeleted, () => {
  void loadFlags()
})

function openFlagCreate(): void {
  flagForm.flag = ''
  flagForm.teamId = ''
  flagForm.specificationKind = ''
  flagForm.specificationId = ''
  flagForm.validStart = ''
  flagForm.validUntil = ''
  flagCreateOpen.value = true
}

function toIso(local: string): string | null {
  if (!local) return null
  const date = new Date(local)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

async function createFlag(): Promise<void> {
  if (!flagForm.flag.trim()) {
    toast.error('请填写 Flag 内容')
    return
  }
  flagCreating.value = true
  const { error } = await adminChallengeBankCreateFlag({
    path: { challengeId },
    body: {
      flag: flagForm.flag,
      teamId: flagForm.teamId.trim() || null,
      specificationKind: flagForm.specificationKind === ''
        ? null
        : (Number(flagForm.specificationKind) as 0 | 1 | 2 | 3),
      specificationId: flagForm.specificationId.trim() || null,
      validStart: toIso(flagForm.validStart),
      validUntil: toIso(flagForm.validUntil),
    },
  })
  flagCreating.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  flagCreateOpen.value = false
  toast.success('Flag 已添加')
  await loadFlags()
}

async function confirmDeleteFlag(): Promise<void> {
  const flag = deletingFlag.value
  if (!flag?.id) return
  flagActionPending.value = true
  const { error } = await adminChallengeBankDeleteFlag({
    path: { challengeId, flagId: flag.id },
  })
  flagActionPending.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  deletingFlag.value = null
  toast.success('Flag 已删除')
  await loadFlags()
}

async function restoreFlag(flag: Flag): Promise<void> {
  if (!flag.id) return
  flagActionPending.value = true
  const { error } = await adminChallengeBankRestoreFlag({
    path: { challengeId, flagId: flag.id },
  })
  flagActionPending.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success('Flag 已恢复')
  await loadFlags()
}

function specificationKindLabel(kind?: number | null): string {
  if (kind === null || kind === undefined) return '—'
  return SPECIFICATION_KINDS[kind]?.label ?? String(kind)
}

// ---------- 权限 ----------
const managersText = ref('')
const permissionsSaving = ref(false)
const newOwnerId = ref('')
const transferOpen = ref(false)
const transferring = ref(false)

function syncPermissions(value: Template): void {
  managersText.value = (value.managerIds ?? []).join('\n')
}

function parseUserIds(text: string): string[] {
  return [...new Set(text.split(/[\s,;]+/).map(id => id.trim()).filter(Boolean))]
}

function conflictMessage(error: unknown): string {
  const code = (error as { code?: string } | undefined)?.code
  switch (code) {
    case 'RevisionConflict':
      return '模板已被他人修改,请刷新后重试'
    case 'OwnerIncludedInManagerSet':
      return '负责人不能同时出现在管理员集合中'
    case 'UserNotFound':
      return '部分用户不存在,请检查用户 ID'
    case 'RoleNotEligible':
      return '部分用户角色不符合要求(需组织者或管理员)'
    case 'ActiveCompetitionModeConflict':
      return '存在进行中的竞赛引用,无法修改模式'
    default:
      return parseApiError(error).message
  }
}

async function savePermissions(): Promise<void> {
  if (!template.value) return
  permissionsSaving.value = true
  const { data, error } = await adminChallengeBankUpdatePermissions({
    path: { challengeId },
    body: {
      managerIds: parseUserIds(managersText.value),
      expectedRevision: template.value.revision ?? 0,
    },
  })
  permissionsSaving.value = false
  if (error) {
    toast.error(conflictMessage(error))
    if ((error as { code?: string }).code === 'RevisionConflict') await loadTemplate()
    return
  }
  if (data) template.value = data
  toast.success('权限已更新')
}

async function transferOwner(): Promise<void> {
  if (!template.value || !newOwnerId.value.trim()) return
  transferring.value = true
  const { data, error } = await adminChallengeBankTransferOwner({
    path: { challengeId },
    body: {
      ownerId: newOwnerId.value.trim(),
      expectedRevision: template.value.revision ?? 0,
    },
  })
  transferring.value = false
  if (error) {
    toast.error(conflictMessage(error))
    if ((error as { code?: string }).code === 'RevisionConflict') await loadTemplate()
    return
  }
  transferOpen.value = false
  newOwnerId.value = ''
  if (data) {
    template.value = data
    syncPermissions(data)
  }
  toast.success('负责人已转让')
}

onMounted(() => {
  if (!canOrganize.value) return
  void (async () => {
    await loadTemplate()
    await Promise.all([loadAttachments(), loadFlags()])
  })()
})
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex items-center gap-2 text-sm text-muted-foreground">
      <NuxtLink to="/admin/challenges" class="hover:underline">题库管理</NuxtLink>
      <span>/</span>
      <span>{{ template?.title ?? challengeId }}</span>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>需要组织者或管理员权限才能管理题库。</AlertDescription>
    </Alert>

    <template v-else>
      <Alert v-if="loadError" variant="destructive">
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>

      <div v-if="loading" class="flex flex-col gap-3">
        <Skeleton class="h-10 w-1/3" />
        <Skeleton class="h-64 w-full" />
      </div>

      <template v-else-if="template">
        <div class="flex flex-wrap items-center justify-between gap-4">
          <div class="flex items-center gap-3">
            <h1 class="text-2xl font-semibold">{{ template.title }}</h1>
            <AdminGameModeBadge :mode="template.mode" />
            <Badge v-if="isDeleted" variant="destructive">已删除</Badge>
          </div>
          <div class="flex items-center gap-2">
            <Button v-if="isDeleted" variant="outline" :disabled="restoring" @click="restoreTemplate">
              <Spinner v-if="restoring" data-icon="inline-start" />
              <RotateCcw v-else data-icon="inline-start" />
              恢复模板
            </Button>
            <AlertDialog v-else>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">
                  <Trash2 data-icon="inline-start" />
                  删除模板
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>删除题目模板</AlertDialogTitle>
                  <AlertDialogDescription>
                    将软删除模板「{{ template.title }}」,之后可以恢复。已被竞赛引用的历史数据不受影响。
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>取消</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" :disabled="deleting" @click="removeTemplate">
                    <Spinner v-if="deleting" data-icon="inline-start" />
                    确认删除
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>

        <Tabs default-value="basic">
          <TabsList>
            <TabsTrigger value="basic">基本信息</TabsTrigger>
            <TabsTrigger value="attachments">
              附件
              <Badge variant="secondary" class="ml-1">{{ attachments.length }}</Badge>
            </TabsTrigger>
            <TabsTrigger value="flags">
              Flags
              <Badge variant="secondary" class="ml-1">{{ flags.length }}</Badge>
            </TabsTrigger>
            <TabsTrigger value="permissions">权限</TabsTrigger>
          </TabsList>

          <TabsContent value="basic">
            <Card>
              <CardContent class="pt-6">
                <form @submit.prevent="save">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="edit-title">标题</FieldLabel>
                      <Input id="edit-title" v-model="form.title" required maxlength="200" :disabled="isDeleted" />
                    </Field>
                    <div class="grid gap-4 sm:grid-cols-2">
                      <Field>
                        <FieldLabel for="edit-mode">游戏模式</FieldLabel>
                        <Select v-model="form.mode" :disabled="isDeleted">
                          <SelectTrigger id="edit-mode" class="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="Ctf">CTF</SelectItem>
                              <SelectItem value="Awd">AWD</SelectItem>
                              <SelectItem value="Awdp">AWDP</SelectItem>
                              <SelectItem value="Koh">KoH</SelectItem>
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                        <FieldDescription>有进行中竞赛引用时模式不可修改。</FieldDescription>
                      </Field>
                      <Field>
                        <FieldLabel for="edit-visibility">可见性</FieldLabel>
                        <Select v-model="form.visibility" :disabled="isDeleted">
                          <SelectTrigger id="edit-visibility" class="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="Private">私有</SelectItem>
                              <SelectItem value="Shared">共享</SelectItem>
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      </Field>
                    </div>
                    <Field>
                      <FieldLabel for="edit-direction">方向</FieldLabel>
                      <Input id="edit-direction" v-model="form.direction" required maxlength="100" :disabled="isDeleted" />
                    </Field>
                    <Field>
                      <FieldLabel for="edit-description">题面</FieldLabel>
                      <Textarea id="edit-description" v-model="form.description" rows="8" :disabled="isDeleted" />
                    </Field>
                    <Field :data-invalid="!!definitionJsonError || undefined">
                      <FieldLabel for="edit-definition-json">定义 JSON</FieldLabel>
                      <Textarea
                        id="edit-definition-json"
                        v-model="form.definitionJson"
                        rows="10"
                        class="font-mono text-sm"
                        :aria-invalid="!!definitionJsonError || undefined"
                        :disabled="isDeleted"
                      />
                      <FieldDescription>Runtime / Checker / Flag 注入定义;修改对未来启动的实例生效。</FieldDescription>
                      <FieldError v-if="definitionJsonError">{{ definitionJsonError }}</FieldError>
                    </Field>
                    <div class="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                      <span>修订版本 {{ template.revision ?? 0 }}</span>
                      <span>创建 <AdminDateTime :value="template.createdAt" /></span>
                      <span>更新 <AdminDateTime :value="template.updatedAt" /></span>
                      <span v-if="template.deletedAt">删除 <AdminDateTime :value="template.deletedAt" /></span>
                    </div>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button type="submit" :disabled="saving || !!definitionJsonError">
                        <Spinner v-if="saving" data-icon="inline-start" />
                        保存修改
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="attachments">
            <Card>
              <CardHeader class="flex flex-row items-center justify-between gap-4">
                <div class="flex items-center gap-2">
                  <Switch id="attachments-include-deleted" v-model:checked="attachmentsIncludeDeleted" />
                  <Label for="attachments-include-deleted">显示已删除</Label>
                </div>
                <div>
                  <input
                    ref="uploadInput"
                    type="file"
                    class="hidden"
                    @change="uploadAttachment"
                  >
                  <Button :disabled="uploading || isDeleted" @click="uploadInput?.click()">
                    <Spinner v-if="uploading" data-icon="inline-start" />
                    <Upload v-else data-icon="inline-start" />
                    上传附件
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                <div v-if="attachmentsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="attachments.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>暂无附件</EmptyTitle>
                    <EmptyDescription>上传题目所需的附件文件。</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <Table v-else>
                  <TableHeader>
                    <TableRow>
                      <TableHead>文件名</TableHead>
                      <TableHead>类型</TableHead>
                      <TableHead>大小</TableHead>
                      <TableHead>上传时间</TableHead>
                      <TableHead>状态</TableHead>
                      <TableHead class="text-right">操作</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="attachment in attachments" :key="attachment.id">
                      <TableCell class="font-medium">
                        <span class="inline-flex items-center gap-2">
                          <Paperclip class="size-4 text-muted-foreground" />
                          {{ attachment.fileName }}
                        </span>
                      </TableCell>
                      <TableCell class="text-muted-foreground">{{ attachment.contentType }}</TableCell>
                      <TableCell>{{ formatBytes(attachment.byteLength) }}</TableCell>
                      <TableCell>
                        <AdminDateTime :value="attachment.createdAt" />
                      </TableCell>
                      <TableCell>
                        <Badge v-if="attachment.deletedAt" variant="destructive">已删除</Badge>
                        <Badge v-else variant="secondary">正常</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <div class="flex justify-end gap-2">
                          <template v-if="attachment.deletedAt">
                            <Button
                              size="sm"
                              variant="outline"
                              :disabled="attachmentActionPending"
                              @click="restoreAttachment(attachment)"
                            >
                              恢复
                            </Button>
                          </template>
                          <template v-else>
                            <Button size="sm" variant="outline" @click="openAttachmentEdit(attachment)">
                              编辑
                            </Button>
                            <Button size="sm" variant="destructive" @click="deletingAttachment = attachment">
                              删除
                            </Button>
                          </template>
                        </div>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="flags">
            <Card>
              <CardHeader class="flex flex-row items-center justify-between gap-4">
                <div class="flex items-center gap-2">
                  <Switch id="flags-include-deleted" v-model:checked="flagsIncludeDeleted" />
                  <Label for="flags-include-deleted">显示已删除</Label>
                </div>
                <Button :disabled="isDeleted" @click="openFlagCreate">
                  新增 Flag
                </Button>
              </CardHeader>
              <CardContent>
                <div v-if="flagsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="flags.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>暂无 Flag</EmptyTitle>
                    <EmptyDescription>添加模板级静态 Flag。</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <Table v-else>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Flag</TableHead>
                      <TableHead>规格</TableHead>
                      <TableHead>有效期</TableHead>
                      <TableHead>状态</TableHead>
                      <TableHead class="text-right">操作</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="flag in flags" :key="flag.id">
                      <TableCell class="max-w-md truncate font-mono text-sm" :title="flag.flag">
                        {{ flag.flag }}
                      </TableCell>
                      <TableCell>
                        <Badge v-if="flag.specificationKind !== null && flag.specificationKind !== undefined" variant="outline">
                          {{ specificationKindLabel(flag.specificationKind) }}
                        </Badge>
                        <span v-else class="text-muted-foreground">—</span>
                      </TableCell>
                      <TableCell class="text-sm text-muted-foreground">
                        <template v-if="flag.validStart || flag.validUntil">
                          <AdminDateTime :value="flag.validStart" />
                          至
                          <AdminDateTime :value="flag.validUntil" />
                        </template>
                        <span v-else>长期有效</span>
                      </TableCell>
                      <TableCell>
                        <Badge v-if="flag.deletedAt" variant="destructive">已删除</Badge>
                        <Badge v-else variant="secondary">正常</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <Button
                          v-if="flag.deletedAt"
                          size="sm"
                          variant="outline"
                          :disabled="flagActionPending"
                          @click="restoreFlag(flag)"
                        >
                          恢复
                        </Button>
                        <Button v-else size="sm" variant="destructive" @click="deletingFlag = flag">
                          删除
                        </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="permissions">
            <div class="grid gap-6 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle>模板权限</CardTitle>
                  <CardDescription>负责人拥有全部权限;管理员可编辑模板内容。</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel>负责人 ID</FieldLabel>
                      <Input :model-value="template.ownerId" disabled class="font-mono text-sm" />
                    </Field>
                    <Field>
                      <FieldLabel for="managers">管理员 ID 列表</FieldLabel>
                      <Textarea
                        id="managers"
                        v-model="managersText"
                        rows="5"
                        class="font-mono text-sm"
                        placeholder="每行一个用户 UUID,或用逗号分隔"
                        :disabled="isDeleted"
                      />
                      <FieldDescription>负责人不能包含在管理员集合中;用户需为组织者或管理员角色。</FieldDescription>
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button :disabled="permissionsSaving" @click="savePermissions">
                        <Spinner v-if="permissionsSaving" data-icon="inline-start" />
                        保存权限
                      </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>转让负责人</CardTitle>
                  <CardDescription>将模板负责人转让给其他组织者或管理员,立即生效。</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="new-owner">新负责人用户 ID</FieldLabel>
                      <Input
                        id="new-owner"
                        v-model="newOwnerId"
                        class="font-mono text-sm"
                        placeholder="用户 UUID"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button variant="destructive" :disabled="!newOwnerId.trim()" @click="transferOpen = true">
                        转让负责人
                      </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
              </Card>
            </div>
          </TabsContent>
        </Tabs>
      </template>
    </template>

    <Dialog v-model:open="attachmentEditOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>编辑附件</DialogTitle>
          <DialogDescription>仅修改元数据,不替换文件内容。</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="attachment-name">文件名</FieldLabel>
            <Input id="attachment-name" v-model="attachmentEditForm.fileName" required />
          </Field>
          <Field>
            <FieldLabel for="attachment-type">Content-Type</FieldLabel>
            <Input id="attachment-type" v-model="attachmentEditForm.contentType" required placeholder="application/octet-stream" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="attachmentEditOpen = false">取消</Button>
          <Button :disabled="attachmentSaving || !attachmentEditForm.fileName.trim()" @click="saveAttachment">
            <Spinner v-if="attachmentSaving" data-icon="inline-start" />
            保存
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!deletingAttachment" @update:open="(open: boolean) => { if (!open) deletingAttachment = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>删除附件</AlertDialogTitle>
          <AlertDialogDescription>
            将软删除附件「{{ deletingAttachment?.fileName }}」,之后可以恢复。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="attachmentActionPending" @click="confirmDeleteAttachment">
            <Spinner v-if="attachmentActionPending" data-icon="inline-start" />
            确认删除
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="flagCreateOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新增 Flag</DialogTitle>
          <DialogDescription>模板级静态 Flag,实例化到竞赛时可被引用。</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="flag-value">Flag 内容</FieldLabel>
            <Input id="flag-value" v-model="flagForm.flag" required class="font-mono text-sm" placeholder="flag{...}" />
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="flag-spec-kind">规格类型(可选)</FieldLabel>
              <Select v-model="flagForm.specificationKind">
                <SelectTrigger id="flag-spec-kind" class="w-full">
                  <SelectValue placeholder="无" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem v-for="kind in SPECIFICATION_KINDS" :key="kind.value" :value="kind.value">
                      {{ kind.label }}
                    </SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="flag-spec-id">规格 ID(可选)</FieldLabel>
              <Input id="flag-spec-id" v-model="flagForm.specificationId" class="font-mono text-sm" />
            </Field>
          </div>
          <Field>
            <FieldLabel for="flag-team">队伍 ID(可选,AWD/AWDP 队伍专属)</FieldLabel>
            <Input id="flag-team" v-model="flagForm.teamId" class="font-mono text-sm" />
          </Field>
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
          <Button variant="outline" @click="flagCreateOpen = false">取消</Button>
          <Button :disabled="flagCreating || !flagForm.flag.trim()" @click="createFlag">
            <Spinner v-if="flagCreating" data-icon="inline-start" />
            添加
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!deletingFlag" @update:open="(open: boolean) => { if (!open) deletingFlag = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>删除 Flag</AlertDialogTitle>
          <AlertDialogDescription>
            将软删除该 Flag({{ deletingFlag?.flag }}),之后可以恢复。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="flagActionPending" @click="confirmDeleteFlag">
            <Spinner v-if="flagActionPending" data-icon="inline-start" />
            确认删除
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="transferOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>转让负责人</AlertDialogTitle>
          <AlertDialogDescription>
            将把模板「{{ template?.title }}」的负责人转让给用户 {{ newOwnerId }},你将失去负责人身份。此操作立即生效。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transferOwner">
            <Spinner v-if="transferring" data-icon="inline-start" />
            确认转让
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
