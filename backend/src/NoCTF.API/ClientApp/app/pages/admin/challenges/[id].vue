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
  adminChallengeBankUpdatePermissions,
  adminChallengeBankUpdateTemplate,
  adminChallengeBankUploadAttachment,
  adminChallengeBankUploadRandomAttachmentBatch,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse,
  NoCtfapiEndpointsAdministrationChallengeBankRandomAttachmentBatchFailureResponse,
} from '~/api'
import { challengeTemplateWriteErrorMessage } from '~/lib/challenge-template-error'
import { defaultDefinitionJson, FlagSource, normalizeDefinitionJson } from '~/utils/game-config'

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

const MODE_NAMES: NoCtfapiEndpointsCompetitionsGameModeProtocol[] = ['Ctf', 'Awd', 'Awdp', 'Koh']

// ---------- 基本信息 ----------
const form = reactive({
  title: '',
  mode: 'Ctf' as NoCtfapiEndpointsCompetitionsGameModeProtocol,
  visibility: 'Private' as NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol,
  direction: '',
  description: '',
  definitionJson: '{}',
})
const saving = ref(false)
const deleting = ref(false)
const restoring = ref(false)

const isDeleted = computed(() => !!template.value?.deletedAt)

// 题目定义:definitionJson 字符串仍是单一事实源,切片组件直接修改共享 model。
const { model: definitionModel, parseFailed: definitionParseFailed } = useDefinitionModel(
  () => form.definitionJson,
  () => form.mode,
  (json) => { form.definitionJson = json },
)

// 「模式定义」Tab 是否有适用块;无块时显示空态。
const hasModeDefinition = computed(() => {
  if (form.mode === 'Awd' || form.mode === 'Awdp') return true
  if (form.mode === 'Ctf') return definitionModel.value?.runtime?.flagSource === FlagSource.PerTeam
  return false
})

const usesRuntimeFlagInjection = computed(() =>
  (form.mode === 'Ctf' || form.mode === 'Awdp')
  && definitionModel.value?.runtime?.flagSource === FlagSource.PerTeam,
)

// 依赖运行环境的模式定义块在未启用运行环境时不生效。
const runtimeDisabled = computed(() => definitionModel.value !== null && definitionModel.value.runtime === null)

function changeMode(value: unknown): void {
  if (value !== 'Ctf' && value !== 'Awd' && value !== 'Awdp' && value !== 'Koh') return
  form.definitionJson = defaultDefinitionJson(value)
  form.mode = value
}

function resetDefinitionToCurrentMode(): void {
  form.definitionJson = defaultDefinitionJson(form.mode)
  toast.info(translate('已载入当前模式默认题目定义，请保存修改'))
}

function syncForm(value: Template): void {
  form.title = value.title ?? ''
  form.visibility = value.visibility ?? 'Private'
  form.direction = value.direction ?? ''
  form.description = value.description ?? ''
  // Keep definitionJson ahead of mode while loading existing templates.
  // useDefinitionModel watches mode and serializes the current parsed model;
  // setting mode first can briefly serialize the empty initial CTF model as the
  // loaded mode and hide persisted runtime/checker settings in the editor.
  form.definitionJson = value.definitionJson ?? '{}'
  form.mode = value.mode ?? 'Ctf'
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
    loadError.value = parseApiError(error, translate("模板不存在或加载失败")).message
    return
  }
  template.value = data
  syncForm(data)
  syncPermissions(data)
}

async function save(): Promise<void> {
  if (!template.value) return
  const normalizedDefinition = normalizeDefinitionJson(form.mode, form.definitionJson)
  if (!normalizedDefinition) {
    toast.error(translate('题目定义格式无效,请检查题目定义配置'))
    return
  }
  saving.value = true
  const { data, error } = await adminChallengeBankUpdateTemplate({
    path: { challengeId },
    body: {
      title: form.title.trim(),
      mode: form.mode,
      visibility: form.visibility,
      direction: form.direction.trim(),
      description: form.description.trim() || null,
      definitionJson: normalizedDefinition,
    },
  })
  saving.value = false
  if (error) {
    toast.error(challengeTemplateWriteErrorMessage(error))
    return
  }
  if (data) {
    template.value = data
    syncForm(data)
  }
  toast.success(translate("已保存"))
}

async function removeTemplate(): Promise<void> {
  deleting.value = true
  const { error } = await adminChallengeBankDeleteTemplate({ path: { challengeId } })
  deleting.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success(translate("模板已删除"))
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
  toast.success(translate("模板已恢复"))
  await loadTemplate()
}

// ---------- 附件 ----------
const attachments = ref<Attachment[]>([])
const attachmentsLoading = ref(false)
const attachmentsIncludeDeleted = ref(false)
const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')
const uploading = ref(false)
const uploadInput = ref<HTMLInputElement | null>(null)
const randomBatchOpen = ref(false)
const randomUploading = ref(false)
const randomDownloadFileName = ref('challenge.zip')
const randomFiles = ref<File[]>([])
const randomUploadInput = ref<HTMLInputElement | null>(null)
const deletingAttachment = ref<Attachment | null>(null)
const attachmentActionPending = ref(false)

function requestAttachmentDeliveryPolicy(value: unknown): void {
  if (value !== 'All' && value !== 'RandomOnePerTeam') return
  if (value === attachmentDeliveryPolicy.value) return
  if (value === 'RandomOnePerTeam') {
    randomBatchOpen.value = true
    return
  }
  toast.info(translate('请先删除所有随机附件变体，系统将自动恢复全部附件模式'))
}

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
  attachmentDeliveryPolicy.value = data?.deliveryPolicy ?? 'All'
  attachments.value = data?.items ?? []
}

watch(attachmentsIncludeDeleted, () => {
  void loadAttachments()
})

async function uploadAttachment(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const files = [...(input.files ?? [])]
  input.value = ''
  if (files.length === 0) return
  uploading.value = true
  let failed: unknown
  for (const file of files) {
    const { error } = await adminChallengeBankUploadAttachment({
      path: { challengeId },
      body: { file },
    })
    if (error) {
      failed = error
      break
    }
  }
  uploading.value = false
  if (failed) {
    toast.error(parseApiError(failed).message)
    await loadAttachments()
    return
  }
  toast.success(translate('已上传 {count} 个附件', { count: files.length }))
  await loadAttachments()
}

function selectRandomFiles(event: Event): void {
  const input = event.target as HTMLInputElement
  randomFiles.value = [...(input.files ?? [])]
  input.value = ''
}

async function uploadRandomBatch(): Promise<void> {
  if (!randomDownloadFileName.value.trim() || randomFiles.value.length === 0) return
  randomUploading.value = true
  const { error } = await adminChallengeBankUploadRandomAttachmentBatch({
    path: { challengeId },
    body: {
      downloadFileName: randomDownloadFileName.value,
      files: randomFiles.value,
    },
  })
  randomUploading.value = false
  if (error) {
    toast.error(randomAttachmentErrorMessage(error))
    return
  }
  toast.success(translate('已创建 {count} 个随机附件变体', { count: randomFiles.value.length }))
  randomFiles.value = []
  randomBatchOpen.value = false
  await Promise.all([loadAttachments(), loadFlags()])
}

function randomAttachmentErrorMessage(error: unknown): string {
  const failure = error as NoCtfapiEndpointsAdministrationChallengeBankRandomAttachmentBatchFailureResponse
  switch (failure.code) {
    case 'UploadTooLarge': return translate('某个附件变体超过上传大小限制')
    case 'InvalidFileName': return translate('统一下载文件名无效')
    case 'InvalidVariantFileName': return translate('原始文件名不是有效的精确 Flag')
    case 'EmptyBatch': return translate('请至少选择一个附件变体')
    case 'DuplicateFlag': return translate('附件变体的 Flag 必须唯一')
    case 'DeliveryModeConflict': return translate('普通附件或静态 Flag 不能与每队随机附件混用')
    case 'BatchStorageFailed': return translate('附件存储失败，整批未生效')
    case 'BatchPersistenceFailed': return translate('附件批次保存失败，整批未生效')
    case 'ResourceIdConflict': return translate('附件资源标识冲突，请重新上传')
    default: return parseApiError(error).message
  }
}

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
  toast.success(translate("附件已删除"))
  await Promise.all([loadAttachments(), loadFlags()])
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
  toast.success(translate("附件已恢复"))
  await Promise.all([loadAttachments(), loadFlags()])
}

function formatBytes(value?: number): string {
  if (value === undefined || value === null) return '—'
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / 1024 / 1024).toFixed(1)} MB`
}

// ---------- Flags ----------
const flags = ref<Flag[]>([])
const supportsRegularExpression = ref(false)
const flagsLoading = ref(false)
const flagsIncludeDeleted = ref(false)
const flagCreateOpen = ref(false)
const flagCreating = ref(false)
const flagForm = reactive({
  flag: '',
  matchKind: 'Exact' as NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol,
})
const deletingFlag = ref<Flag | null>(null)
const flagActionPending = ref(false)

const staticFlags = computed(() => flags.value.filter(flag => !flag.systemManaged))
const systemFlags = computed(() => flags.value.filter(flag => flag.systemManaged))

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
  supportsRegularExpression.value = data?.supportsRegularExpression ?? false
  if (!supportsRegularExpression.value && flagForm.matchKind === 'RegularExpression')
    flagForm.matchKind = 'Exact'
}

watch(flagsIncludeDeleted, () => {
  void loadFlags()
})

function openFlagCreate(): void {
  flagForm.flag = ''
  flagForm.matchKind = 'Exact'
  flagCreateOpen.value = true
}

async function createFlag(): Promise<void> {
  if (!flagForm.flag.trim()) {
    toast.error(translate("请填写 Flag 内容"))
    return
  }
  flagCreating.value = true
  const { error } = await adminChallengeBankCreateFlag({
    path: { challengeId },
    body: {
      flag: flagForm.flag,
      matchKind: flagForm.matchKind,
    },
  })
  flagCreating.value = false
  if (error) {
    toast.error(challengeFlagErrorMessage(error))
    return
  }
  flagCreateOpen.value = false
  toast.success(translate("Flag 已添加"))
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
    toast.error(challengeFlagErrorMessage(error))
    return
  }
  deletingFlag.value = null
  toast.success(translate("Flag 已删除"))
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
    toast.error(challengeFlagErrorMessage(error))
    return
  }
  toast.success(translate("Flag 已恢复"))
  await loadFlags()
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
  const code = parseApiError(error).code
  switch (code) {
    case 'OwnerIncludedInManagerSet':
      return translate("负责人不能同时出现在管理员集合中")
    case 'UserNotFound':
      return translate("部分用户不存在,请检查用户 ID")
    case 'RoleNotEligible':
      return translate("部分用户角色不符合要求(需组织者或管理员)")
    case 'ActiveCompetitionModeConflict':
      return translate("存在进行中的竞赛引用,无法修改模式")
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
    },
  })
  permissionsSaving.value = false
  if (error) {
    toast.error(conflictMessage(error))
    return
  }
  if (data) template.value = data
  toast.success(translate("权限已更新"))
}

async function transferOwner(): Promise<void> {
  if (!template.value || !newOwnerId.value.trim()) return
  transferring.value = true
  const { data, error } = await adminChallengeBankTransferOwner({
    path: { challengeId },
    body: {
      ownerId: newOwnerId.value.trim(),
    },
  })
  transferring.value = false
  if (error) {
    toast.error(conflictMessage(error))
    return
  }
  transferOpen.value = false
  newOwnerId.value = ''
  if (data) {
    template.value = data
    syncPermissions(data)
  }
  toast.success(translate("负责人已转让"))
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
      <NuxtLink to="/admin/challenges" class="hover:underline">{{ $t('题库管理') }}</NuxtLink>
      <span>/</span>
      <span>{{ template?.title ?? challengeId }}</span>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('需要组织者或管理员权限才能管理题库。') }}</AlertDescription>
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
            <Badge v-if="isDeleted" variant="destructive">{{ $t('已删除') }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <Button v-if="isDeleted" variant="outline" :disabled="restoring" @click="restoreTemplate">
              <Spinner v-if="restoring" data-icon="inline-start" />
              <RotateCcw v-else data-icon="inline-start" /> {{ $t('恢复模板') }} </Button>
            <AlertDialog v-else>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">
                  <Trash2 data-icon="inline-start" /> {{ $t('删除模板') }} </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ $t('删除题目模板') }}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {{ $t('将软删除模板「{title}」，之后可以恢复。已被竞赛引用的历史数据不受影响。', { title: template.title ?? '—' }) }}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" :disabled="deleting" @click="removeTemplate">
                    <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('确认删除') }} </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>

        <Tabs default-value="basic">
          <TabsList>
            <TabsTrigger value="basic">{{ $t('基本信息') }}</TabsTrigger>
            <TabsTrigger value="runtime">{{ $t('运行环境') }}</TabsTrigger>
            <TabsTrigger value="definition">{{ $t('模式定义') }}</TabsTrigger>
            <TabsTrigger value="attachments"> {{ $t('附件') }} <Badge variant="secondary" class="ml-1">{{ attachments.length }}</Badge>
            </TabsTrigger>
            <TabsTrigger value="flags">
              Flags
              <Badge variant="secondary" class="ml-1">{{ flags.length }}</Badge>
            </TabsTrigger>
            <TabsTrigger value="permissions">{{ $t('权限') }}</TabsTrigger>
          </TabsList>

          <TabsContent value="basic">
            <Card>
              <CardContent class="pt-6">
                <form @submit.prevent="save">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="edit-title">{{ $t('标题') }}</FieldLabel>
                      <Input id="edit-title" v-model="form.title" required maxlength="200" :disabled="isDeleted" />
                    </Field>
                    <div class="grid gap-4 sm:grid-cols-2">
                      <Field>
                        <FieldLabel for="edit-mode">{{ $t('游戏模式') }}</FieldLabel>
                        <Select :model-value="form.mode" :disabled="isDeleted" @update:model-value="changeMode">
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
                        <FieldDescription>{{ $t('有进行中竞赛引用时模式不可修改。') }}</FieldDescription>
                      </Field>
                      <Field>
                        <FieldLabel for="edit-visibility">{{ $t('可见性') }}</FieldLabel>
                        <Select v-model="form.visibility" :disabled="isDeleted">
                          <SelectTrigger id="edit-visibility" class="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="Private">{{ $t('私有') }}</SelectItem>
                              <SelectItem value="Shared">{{ $t('共享') }}</SelectItem>
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      </Field>
                    </div>
                    <Field>
                      <FieldLabel for="edit-direction">{{ $t('方向') }}</FieldLabel>
                      <Input id="edit-direction" v-model="form.direction" required maxlength="100" :disabled="isDeleted" />
                    </Field>
                    <Field>
                      <FieldLabel for="edit-description">{{ $t('题面') }}</FieldLabel>
                      <Textarea id="edit-description" v-model="form.description" rows="8" :disabled="isDeleted" />
                    </Field>
                    <div class="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                      <span>{{ $t('创建') }} <AdminDateTime :value="template.createdAt" /></span>
                      <span>{{ $t('更新') }} <AdminDateTime :value="template.updatedAt" /></span>
                      <span v-if="template.deletedAt">{{ $t('删除') }} <AdminDateTime :value="template.deletedAt" /></span>
                    </div>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button type="submit" :disabled="saving">
                        <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('保存修改') }} </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="runtime">
            <Card>
              <CardContent class="pt-6">
                <Alert v-if="definitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('现有定义 JSON 无法解析,可能是历史遗留数据。请重置为当前模式默认定义后补齐运行环境配置。') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('重置为当前模式默认题目定义') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="definitionModel">
                  <div v-if="!isDeleted" class="flex flex-wrap items-center gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('重置为当前模式默认题目定义') }}
                    </Button>
                    <span class="text-xs text-muted-foreground">{{ $t('重置后请返回基本信息保存修改') }}</span>
                  </div>
                  <DefinitionRuntimeSection :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  <FieldDescription>{{ $t('Runtime 定义修改对未来启动的实例生效。') }}</FieldDescription>
                </FieldGroup>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="definition">
            <Card>
              <CardContent class="pt-6">
                <Alert v-if="definitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('现有定义 JSON 无法解析,可能是历史遗留数据。请重置为当前模式默认定义后补齐模式配置。') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('重置为当前模式默认题目定义') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="definitionModel">
                  <div v-if="!isDeleted" class="flex flex-wrap items-center gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('重置为当前模式默认题目定义') }}
                    </Button>
                    <span class="text-xs text-muted-foreground">{{ $t('重置后请返回基本信息保存修改') }}</span>
                  </div>
                  <Alert v-if="hasModeDefinition && runtimeDisabled">
                    <AlertDescription>{{ $t('运行环境未启用时,以下配置不会生效。') }}</AlertDescription>
                  </Alert>
                  <template v-if="form.mode === 'Awd'">
                    <DefinitionFlagInjectionSection :model="definitionModel" :disabled="isDeleted" />
                    <DefinitionCheckerSection :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                    <DefinitionFlagTemplateSection :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  </template>
                  <template v-else-if="form.mode === 'Awdp'">
                    <DefinitionPatchSection :model="definitionModel" :disabled="isDeleted" />
                    <DefinitionCheckerSection :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  </template>
                  <DefinitionFlagTemplateSection
                    v-else-if="form.mode === 'Ctf'"
                    :model="definitionModel"
                    :mode="form.mode"
                    :disabled="isDeleted"
                  />
                  <Empty v-if="!hasModeDefinition">
                    <EmptyHeader>
                      <EmptyTitle>{{ $t('该模式没有额外模式定义') }}</EmptyTitle>
                    </EmptyHeader>
                  </Empty>
                  <FieldDescription v-else> {{ $t('定义修改对未来启动/重置的实例生效,已存在的运行实例不受影响。') }} </FieldDescription>
                </FieldGroup>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="attachments">
            <Card>
              <CardHeader class="gap-4">
                <div class="flex flex-wrap items-center justify-between gap-4">
                  <div>
                    <CardTitle>{{ $t('附件交付') }}</CardTitle>
                    <CardDescription>
                      {{ attachmentDeliveryPolicy === 'RandomOnePerTeam'
                        ? $t('每支队伍首次下载时固定获得一个随机附件变体。')
                        : $t('选手可以查看并下载全部普通附件。') }}
                    </CardDescription>
                  </div>
                  <Badge variant="outline">
                    {{ attachmentDeliveryPolicy === 'RandomOnePerTeam' ? $t('每队随机一个') : $t('全部附件') }}
                  </Badge>
                </div>
                <div class="flex flex-wrap items-end justify-between gap-3">
                  <div class="flex items-center gap-2">
                    <Switch id="attachments-include-deleted" v-model="attachmentsIncludeDeleted" />
                    <Label for="attachments-include-deleted">{{ $t('显示已删除') }}</Label>
                  </div>
                  <div class="flex flex-wrap items-end gap-2">
                    <div class="grid min-w-48 gap-1.5">
                      <Label for="attachment-delivery-policy">{{ $t('交付模式') }}</Label>
                      <Select
                        :model-value="attachmentDeliveryPolicy"
                        :disabled="isDeleted || uploading || randomUploading"
                        @update:model-value="requestAttachmentDeliveryPolicy"
                      >
                        <SelectTrigger id="attachment-delivery-policy" class="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="All">{{ $t('全部附件') }}</SelectItem>
                          <SelectItem value="RandomOnePerTeam">{{ $t('每队随机一个') }}</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <template v-if="attachmentDeliveryPolicy === 'All'">
                      <input ref="uploadInput" type="file" multiple class="hidden" @change="uploadAttachment">
                      <Button :disabled="uploading || isDeleted" @click="uploadInput?.click()">
                        <Spinner v-if="uploading" data-icon="inline-start" />
                        <Upload v-else data-icon="inline-start" /> {{ $t('上传普通附件') }}
                      </Button>
                      <Button
                        variant="outline"
                        :disabled="isDeleted || attachments.some(item => !item.deletedAt)"
                        @click="randomBatchOpen = true"
                      >
                        {{ $t('创建每队随机附件') }}
                      </Button>
                    </template>
                    <Button v-else :disabled="isDeleted" @click="randomBatchOpen = true">
                      {{ $t('上传随机附件批次') }}
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                <div v-if="attachmentsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="attachments.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('暂无附件') }}</EmptyTitle>
                    <EmptyDescription>{{ $t('上传题目所需的附件文件。') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <Table v-else>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{{ $t('文件名') }}</TableHead>
                      <TableHead v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'">{{ $t('精确 Flag') }}</TableHead>
                      <TableHead v-else>{{ $t('类型') }}</TableHead>
                      <TableHead>{{ $t('哈希') }}</TableHead>
                      <TableHead>{{ $t('大小') }}</TableHead>
                      <TableHead>{{ $t('上传时间') }}</TableHead>
                      <TableHead>{{ $t('状态') }}</TableHead>
                      <TableHead class="text-right">{{ $t('操作') }}</TableHead>
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
                      <TableCell v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'" class="font-mono text-sm">
                        {{ attachment.exactFlag ?? '—' }}
                      </TableCell>
                      <TableCell v-else class="text-muted-foreground">{{ attachment.contentType }}</TableCell>
                      <TableCell
                        class="font-mono text-xs tabular-nums text-muted-foreground"
                        :title="attachment.sha256"
                      >
                        {{ attachment.sha256?.slice(0, 8) ?? '—' }}
                      </TableCell>
                      <TableCell>{{ formatBytes(attachment.byteLength) }}</TableCell>
                      <TableCell>
                        <AdminDateTime :value="attachment.createdAt" />
                      </TableCell>
                      <TableCell>
                        <Badge v-if="attachment.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('正常') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <div class="flex justify-end gap-2">
                          <template v-if="attachment.deletedAt">
                            <Button
                              size="sm"
                              variant="outline"
                              :disabled="attachmentActionPending"
                              @click="restoreAttachment(attachment)"
                            > {{ $t('恢复') }} </Button>
                          </template>
                          <template v-else>
                            <Button size="sm" variant="destructive" @click="deletingAttachment = attachment"> {{ $t('删除') }} </Button>
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
                  <Switch id="flags-include-deleted" v-model="flagsIncludeDeleted" />
                  <Label for="flags-include-deleted">{{ $t('显示已删除') }}</Label>
                </div>
                <Button v-if="!usesRuntimeFlagInjection" :disabled="isDeleted" @click="openFlagCreate">
                  {{ $t('新增 Flag') }}
                </Button>
              </CardHeader>
              <CardContent>
                <Alert v-if="usesRuntimeFlagInjection" class="mb-4">
                  <AlertDescription>
                    {{ $t('该题使用运行环境动态 Flag。平台会为每支队伍生成 Flag，并在启动容器时注入环境变量；无需维护精确或正则 Flag。') }}
                  </AlertDescription>
                </Alert>
                <div v-if="flagsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="staticFlags.length === 0 && systemFlags.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('暂无 Flag') }}</EmptyTitle>
                    <EmptyDescription v-if="!usesRuntimeFlagInjection">{{ $t('添加模板级静态 Flag。') }}</EmptyDescription>
                    <EmptyDescription v-else>{{ $t('动态 Flag 将在队伍启动容器时自动生成。') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <div v-else class="space-y-6">
                  <section v-if="staticFlags.length > 0" class="space-y-2">
                    <h3 class="text-sm font-semibold">{{ $t('静态 Flag') }}</h3>
                    <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Flag</TableHead>
                      <TableHead>{{ $t('状态') }}</TableHead>
                      <TableHead class="text-right">{{ $t('操作') }}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="flag in staticFlags" :key="flag.id">
                      <TableCell class="max-w-md" :title="flag.flag">
                        <div class="flex min-w-0 items-center gap-2">
                          <Badge variant="outline">
                            {{ flag.matchKind === 'RegularExpression' ? $t('正则匹配') : $t('精确匹配') }}
                          </Badge>
                          <span class="truncate font-mono text-sm">{{ flag.flag }}</span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('正常') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <Button
                          v-if="flag.deletedAt"
                          size="sm"
                          variant="outline"
                          :disabled="flagActionPending"
                          @click="restoreFlag(flag)"
                        > {{ $t('恢复') }} </Button>
                        <Button v-else size="sm" variant="destructive" @click="deletingFlag = flag"> {{ $t('删除') }} </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                    </Table>
                  </section>

                  <section v-if="systemFlags.length > 0" class="space-y-2">
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
                        <TableRow v-for="flag in systemFlags" :key="flag.id">
                          <TableCell class="max-w-md truncate font-mono text-sm" :title="flag.flag">{{ flag.flag }}</TableCell>
                          <TableCell class="font-mono text-xs">{{ flag.teamId ?? '—' }}</TableCell>
                          <TableCell class="font-mono text-xs">
                            {{ flag.specificationKind ?? '—' }}<span v-if="flag.specificationId"> · {{ flag.specificationId }}</span>
                          </TableCell>
                          <TableCell class="text-sm text-muted-foreground">
                            <template v-if="flag.validStart || flag.validUntil">
                              <AdminDateTime :value="flag.validStart" /> {{ $t('至') }} <AdminDateTime :value="flag.validUntil" />
                            </template>
                            <span v-else>{{ $t('长期有效') }}</span>
                          </TableCell>
                          <TableCell>
                            <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('已失效') }}</Badge>
                            <Badge v-else variant="secondary">{{ $t('已注入') }}</Badge>
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </section>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="permissions">
            <div class="grid gap-6 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('模板权限') }}</CardTitle>
                  <CardDescription>{{ $t('负责人拥有全部权限;管理员可编辑模板内容。') }}</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel>{{ $t('负责人 ID') }}</FieldLabel>
                      <Input :model-value="template.ownerId" disabled class="font-mono text-sm" />
                    </Field>
                    <Field>
                      <FieldLabel for="managers">{{ $t('管理员 ID 列表') }}</FieldLabel>
                      <Textarea
                        id="managers"
                        v-model="managersText"
                        rows="5"
                        class="font-mono text-sm"
                        :placeholder="$t('每行一个用户 UUID,或用逗号分隔')"
                        :disabled="isDeleted"
                      />
                      <FieldDescription>{{ $t('负责人不能包含在管理员集合中;用户需为组织者或管理员角色。') }}</FieldDescription>
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button :disabled="permissionsSaving" @click="savePermissions">
                        <Spinner v-if="permissionsSaving" data-icon="inline-start" /> {{ $t('保存权限') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('转让负责人') }}</CardTitle>
                  <CardDescription>{{ $t('将模板负责人转让给其他组织者或管理员,立即生效。') }}</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="new-owner">{{ $t('新负责人用户 ID') }}</FieldLabel>
                      <Input
                        id="new-owner"
                        v-model="newOwnerId"
                        class="font-mono text-sm"
                        :placeholder="$t('用户 UUID')"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button variant="destructive" :disabled="!newOwnerId.trim()" @click="transferOpen = true"> {{ $t('转让负责人') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
              </Card>
            </div>
          </TabsContent>
        </Tabs>
      </template>
    </template>

    <AlertDialog :open="!!deletingAttachment" @update:open="(open: boolean) => { if (!open) deletingAttachment = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('删除附件') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('将软删除附件「{file}」，之后可以恢复。', { file: deletingAttachment?.fileName ?? '—' }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="attachmentActionPending" @click="confirmDeleteAttachment">
            <Spinner v-if="attachmentActionPending" data-icon="inline-start" /> {{ $t('确认删除') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="randomBatchOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{{ $t('每队随机附件') }}</DialogTitle>
          <DialogDescription>{{ $t('原始文件名将完整解析为精确 Flag；选手只会看到统一下载文件名。') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="random-download-name">{{ $t('选手下载文件名') }}</FieldLabel>
            <Input id="random-download-name" v-model="randomDownloadFileName" placeholder="challenge.zip" />
          </Field>
          <Field>
            <FieldLabel>{{ $t('附件变体') }}</FieldLabel>
            <input ref="randomUploadInput" type="file" multiple class="hidden" @change="selectRandomFiles">
            <Button variant="outline" :disabled="randomUploading" @click="randomUploadInput?.click()">
              <Upload data-icon="inline-start" /> {{ $t('选择多个文件') }}
            </Button>
            <FieldDescription>{{ $t('已选择 {count} 个变体；批次内原始文件名必须唯一。', { count: randomFiles.length }) }}</FieldDescription>
            <div v-if="randomFiles.length" class="max-h-56 overflow-auto rounded-md border p-3">
              <div v-for="file in randomFiles" :key="`${file.name}-${file.size}-${file.lastModified}`" class="flex justify-between gap-4 py-1 text-sm">
                <span class="truncate font-mono">{{ file.name }}</span>
                <span class="shrink-0 text-muted-foreground">{{ formatBytes(file.size) }}</span>
              </div>
            </div>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="randomUploading" @click="randomBatchOpen = false">{{ $t('取消') }}</Button>
          <Button :disabled="randomUploading || !randomDownloadFileName.trim() || randomFiles.length === 0" @click="uploadRandomBatch">
            <Spinner v-if="randomUploading" data-icon="inline-start" /> {{ $t('上传整个批次') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="flagCreateOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('新增 Flag') }}</DialogTitle>
          <DialogDescription>{{ $t('模板级静态 Flag,实例化到竞赛时可被引用。') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
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
              required
              class="font-mono text-sm"
              :placeholder="flagForm.matchKind === 'RegularExpression' ? 'flag\\{[0-9a-f-]{36}\\}' : 'flag{...}'"
            />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="flagCreateOpen = false">{{ $t('取消') }}</Button>
          <Button :disabled="flagCreating || !flagForm.flag.trim()" @click="createFlag">
            <Spinner v-if="flagCreating" data-icon="inline-start" /> {{ $t('添加') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!deletingFlag" @update:open="(open: boolean) => { if (!open) deletingFlag = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('删除 Flag') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('将软删除该 Flag（{flag}），之后可以恢复。', { flag: deletingFlag?.flag ?? '—' }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="flagActionPending" @click="confirmDeleteFlag">
            <Spinner v-if="flagActionPending" data-icon="inline-start" /> {{ $t('确认删除') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="transferOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('转让负责人') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('将把模板「{title}」的负责人转让给用户 {user}，你将失去负责人身份。此操作立即生效。', { title: template?.title ?? '—', user: newOwnerId }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('取消') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transferOwner">
            <Spinner v-if="transferring" data-icon="inline-start" /> {{ $t('确认转让') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
