<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminDeleteCompetition,
  adminFinishCompetition,
  adminForceDeleteCompetition,
  adminGenerateMissingFlags,
  adminHardDeleteCompetition,
  adminMakeCompetitionVisible,
  adminPauseCompetition,
  adminPreviewCompetitionHardDelete,
  adminPublishCompetition,
  adminRestoreCompetition,
  adminResumeCompetition,
  adminStartCompetition,
  adminValidateCompetitionStart,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode,
  NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { startGateErrorMessage } from '~/lib/start-gate-error'

definePageMeta({ middleware: 'auth' })

const { competitionId, competition, canWrite, canManagePermissions, refresh } = useCompetitionAdmin()
const { isAdministrator } = useAuth()

const pendingAction = ref<string | null>(null)
const actionError = ref<string | null>(null)

const status = computed(() => competition.value?.status)
const isDeleted = computed(() => !!competition.value?.deletedAt)

const steps = [
  { value: 'Draft', label: '草稿' },
  { value: 'Visible', label: '可见' },
  { value: 'Published', label: '已发布' },
  { value: 'Running', label: '进行中' },
  { value: 'Finished', label: '已结束' },
]

interface LifecycleAction {
  key: string
  label: string
  visible: boolean
  destructive?: boolean
  confirm?: { title: string; description: string }
  run: () => Promise<{ error?: unknown }>
}

const actions = computed<LifecycleAction[]>(() => [
  {
    key: 'make-visible',
    label: translate("对外可见"),
    visible: status.value === 'Draft',
    run: () => adminMakeCompetitionVisible({ path: { competitionId } }),
  },
  {
    key: 'publish',
    label: translate("发布竞赛"),
    visible: status.value === 'Visible',
    run: () => adminPublishCompetition({ path: { competitionId } }),
  },
  {
    key: 'start',
    label: translate("开始比赛"),
    visible: status.value === 'Published',
    confirm: { title: translate("开始比赛"), description: translate("将立即开始比赛并为队伍预置运行时实例。确认继续?") },
    run: () => adminStartCompetition({ path: { competitionId } }),
  },
  {
    key: 'pause',
    label: translate("暂停比赛"),
    visible: status.value === 'Running',
    run: () => adminPauseCompetition({ path: { competitionId } }),
  },
  {
    key: 'resume',
    label: translate("恢复比赛"),
    visible: status.value === 'Paused',
    run: () => adminResumeCompetition({ path: { competitionId } }),
  },
  {
    key: 'finish',
    label: translate("结束比赛"),
    visible: status.value === 'Published' || status.value === 'Running' || status.value === 'Paused',
    destructive: true,
    confirm: { title: translate("结束比赛"), description: translate("结束比赛不可撤销,将清理全部运行时实例。确认结束?") },
    run: () => adminFinishCompetition({ path: { competitionId } }),
  },
])

const confirmTarget = ref<LifecycleAction | null>(null)

async function execute(action: LifecycleAction) {
  pendingAction.value = action.key
  actionError.value = null
  try {
    const { error } = await action.run()
    if (error) throw error
    toast.success(translate('{action}成功', { action: action.label }))
    await refresh()
  }
  catch (e) {
    actionError.value = parseApiError(e).message
  }
  finally {
    pendingAction.value = null
    confirmTarget.value = null
  }
}

function trigger(action: LifecycleAction) {
  if (action.confirm) {
    confirmTarget.value = action
    return
  }
  void execute(action)
}

// ---- Start gate validation ----
const validating = ref(false)
const validationErrors = ref<NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse[] | null>(null)

async function validateStart() {
  validating.value = true
  validationErrors.value = null
  try {
    const { data, error } = await adminValidateCompetitionStart({ path: { competitionId } })
    if (error) throw error
    validationErrors.value = data?.errors ?? []
    if (validationErrors.value.length === 0) toast.success(translate("启动前检查通过"))
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    validating.value = false
  }
}

// ---- Generate missing flags ----
const generating = ref(false)
const generateFailures = ref<{ competitionChallengeId?: string; teamId?: string; code?: string; description?: string }[]>([])

async function generateMissingFlags() {
  generating.value = true
    generateFailures.value = []
  try {
    const { data, error } = await adminGenerateMissingFlags({ path: { competitionId } })
    if (error) throw error
    generateFailures.value = data?.failures ?? []
    if (generateFailures.value.length === 0) toast.success(translate("缺失 Flag 已全部生成"))
    else toast.warning(translate('生成完成，{count} 项失败', { count: generateFailures.value.length }))
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    generating.value = false
  }
}

// ---- Delete / restore / hard delete ----
const deleting = ref(false)
const restoring = ref(false)
const hardDeleting = ref(false)
const forceDeleting = ref(false)
const deleteConfirm = ref<'soft' | 'hard' | null>(null)
const forceDeleteOpen = ref(false)
const forceDeleteTitle = ref('')
const forceDeleteReason = ref('')
const forceDeleteError = ref<string | null>(null)
const hardDeletePreview = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse | null>(null)
const hardDeletePreviewLoading = ref(false)
const hardDeletePreviewError = ref<string | null>(null)
let hardDeletePreviewRequest = 0

const hardDeleteReferenceLabels: Record<NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode, string> = {
  HistoricalEvent: '永久比赛事件',
  Team: '队伍',
  CompetitionChallenge: '比赛题目',
  GameplayFact: '比赛事实',
  RuntimeInstance: '运行环境',
  PatchUpload: '补丁上传',
  DataExport: '数据导出',
  Notification: '通知与咨询',
  PosterFile: '比赛海报',
  ActiveRuntimeResource: '活动运行环境资源',
}

function hardDeleteReferenceLabel(code?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode) {
  return code ? translate(hardDeleteReferenceLabels[code]) : translate('未知引用')
}

function isHardDeletePreview(value: unknown): value is NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse {
  return !!value && typeof value === 'object' && 'canHardDelete' in value && 'references' in value
}

async function loadHardDeletePreview() {
  const request = ++hardDeletePreviewRequest
  if (!canManagePermissions.value || !competition.value) {
    hardDeletePreview.value = null
    hardDeletePreviewError.value = null
    hardDeletePreviewLoading.value = false
    return
  }
  hardDeletePreviewLoading.value = true
  hardDeletePreviewError.value = null
  try {
    const { data, error } = await adminPreviewCompetitionHardDelete({ path: { competitionId } })
    if (error) throw error
    if (request === hardDeletePreviewRequest) hardDeletePreview.value = data ?? null
  }
  catch (error) {
    if (request === hardDeletePreviewRequest) {
      hardDeletePreviewError.value = parseApiError(error).message
    }
  }
  finally {
    if (request === hardDeletePreviewRequest) hardDeletePreviewLoading.value = false
  }
}

watch(
  [() => competition.value?.id, canManagePermissions],
  () => { void loadHardDeletePreview() },
  { immediate: true },
)

async function softDelete() {
  deleting.value = true
  try {
    const { error } = await adminDeleteCompetition({ path: { competitionId } })
    if (error) throw error
    toast.success(translate("竞赛已删除"))
    await refresh()
    await loadHardDeletePreview()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    deleting.value = false
    deleteConfirm.value = null
  }
}

async function restore() {
  restoring.value = true
  try {
    const { error } = await adminRestoreCompetition({ path: { competitionId } })
    if (error) throw error
    toast.success(translate("竞赛已恢复"))
    await refresh()
    await loadHardDeletePreview()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    restoring.value = false
  }
}

async function hardDelete() {
  hardDeleting.value = true
  try {
    const { error } = await adminHardDeleteCompetition({ path: { competitionId } })
    if (error) throw error
    toast.success(translate("竞赛已彻底删除"))
    await navigateTo('/admin/competitions')
  }
  catch (e) {
    if (isHardDeletePreview(e)) {
      hardDeletePreview.value = e
      toast.error(translate('竞赛仍有永久历史或业务引用，无法彻底删除。'))
    }
    else {
      toast.error(parseApiError(e).message)
    }
  }
  finally {
    hardDeleting.value = false
    deleteConfirm.value = null
  }
}

const forceDeleteValid = computed(() =>
  forceDeleteTitle.value === (competition.value?.title ?? '')
  && forceDeleteReason.value.trim().length >= 8
  && forceDeleteReason.value.trim().length <= 500,
)

function beginForceDelete(): void {
  forceDeleteTitle.value = ''
  forceDeleteReason.value = ''
  forceDeleteError.value = null
  forceDeleteOpen.value = true
}

async function forceDelete(): Promise<void> {
  if (!forceDeleteValid.value) return
  forceDeleting.value = true
  forceDeleteError.value = null
  try {
    const { error } = await adminForceDeleteCompetition({
      path: { competitionId },
      body: {
        confirmationTitle: forceDeleteTitle.value,
        reason: forceDeleteReason.value.trim(),
      },
    })
    if (error) throw error
    forceDeleteOpen.value = false
    toast.success(translate('竞赛及其作用域数据已永久删除，平台审计记录已保留'))
    await navigateTo('/admin/competitions')
  }
  catch (error) {
    forceDeleteError.value = parseApiError(error).message
    await loadHardDeletePreview()
  }
  finally {
    forceDeleting.value = false
  }
}

async function submitDelete() {
  const action = deleteConfirm.value
  if (action === 'hard') await hardDelete()
  else if (action === 'soft') await softDelete()
}
</script>

<template>
  <div v-if="competition" class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('生命周期') }}</CardTitle>
        <CardDescription class="font-mono tabular-nums">
          {{ adminFormatDateTime(competition.startTime) }} ~ {{ adminFormatDateTime(competition.endTime) }}
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-6">
        <div class="flex flex-wrap items-center gap-2">
          <template v-for="(step, i) in steps" :key="step.value">
            <Badge :variant="status === step.value || (step.value === 'Running' && status === 'Paused') ? 'default' : 'outline'">
              {{ step.value === 'Running' && status === 'Paused' ? $t('已暂停') : $t(step.label) }}
            </Badge>
            <span v-if="i < steps.length - 1" class="text-muted-foreground">→</span>
          </template>
        </div>

        <Alert v-if="actionError" variant="destructive">
          <AlertDescription>{{ actionError }}</AlertDescription>
        </Alert>

        <div v-if="canWrite && !isDeleted" class="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" :disabled="validating" @click="validateStart">
            <Spinner v-if="validating" data-icon="inline-start" /> {{ $t('启动前检查') }} </Button>
          <Button
            v-for="a in actions.filter(a => a.visible)"
            :key="a.key"
            size="sm"
            :variant="a.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="trigger(a)"
          >
            <Spinner v-if="pendingAction === a.key" data-icon="inline-start" />
            {{ a.label }}
          </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('当前角色为只读,无法执行生命周期操作') }}</p>

        <div v-if="validationErrors && validationErrors.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(ve, i) in validationErrors" :key="i" variant="destructive">
            <AlertDescription>
              <span class="font-mono text-xs">{{ ve.code }}</span>
              · {{ startGateErrorMessage(ve) }}
              <NuxtLink
                v-if="ve.competitionChallengeId"
                class="underline"
                :to="`/admin/competitions/${competitionId}/challenges/${ve.competitionChallengeId}`"
              > {{ $t('查看题目') }} </NuxtLink>
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite && !isDeleted">
      <CardHeader>
        <CardTitle>{{ $t('Flag 生成') }}</CardTitle>
        <CardDescription>{{ $t('为动态 Flag 题目批量生成缺失的队伍 Flag(幂等)') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <div>
          <Button variant="outline" size="sm" :disabled="generating" @click="generateMissingFlags">
            <Spinner v-if="generating" data-icon="inline-start" /> {{ $t('生成缺失 Flag') }} </Button>
        </div>
        <div v-if="generateFailures && generateFailures.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(f, i) in generateFailures" :key="i" variant="destructive">
            <AlertDescription>
              {{ $t('题目 {challenge} / 队伍 {team}：{description}', { challenge: f.competitionChallengeId ?? '-', team: f.teamId ?? '-', description: f.description ?? f.code ?? '-' }) }}
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite">
      <CardHeader>
        <CardTitle class="text-destructive">{{ $t('危险区') }}</CardTitle>
        <CardDescription v-if="isDeleted">
          {{ $t('此竞赛已于 {time} 软删除。恢复不会丢失历史数据。', { time: adminFormatDateTime(competition.deletedAt) }) }}
        </CardDescription>
        <CardDescription v-else>{{ $t('软删除可恢复；物理删除是独立操作，只适用于从未产生历史和业务引用的空竞赛。') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col items-start gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <Button v-if="!isDeleted" variant="outline" size="sm" :disabled="deleting" @click="deleteConfirm = 'soft'"> {{ $t('软删除竞赛') }} </Button>
          <template v-if="isDeleted && canManagePermissions">
            <Button variant="outline" size="sm" :disabled="restoring" @click="restore">
              <Spinner v-if="restoring" data-icon="inline-start" /> {{ $t('恢复已删除竞赛') }} </Button>
          </template>
          <Button
            v-if="canManagePermissions && hardDeletePreview?.canHardDelete"
            variant="destructive"
            size="sm"
            :disabled="hardDeleting"
            @click="deleteConfirm = 'hard'"
          > {{ $t('彻底删除') }} </Button>
          <Button
            v-if="isAdministrator"
            variant="destructive"
            size="sm"
            :disabled="forceDeleting"
            @click="beginForceDelete"
          > {{ $t('强制级联删除') }} </Button>
        </div>

        <div v-if="canManagePermissions && hardDeletePreviewLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
          <Spinner data-icon="inline-start" /> {{ $t('正在检查永久删除影响') }}
        </div>
        <Alert v-else-if="canManagePermissions && hardDeletePreviewError" variant="destructive" class="w-full">
          <AlertDescription>{{ hardDeletePreviewError }}</AlertDescription>
        </Alert>
        <Alert v-else-if="canManagePermissions && hardDeletePreview && !hardDeletePreview.canHardDelete" class="w-full">
          <AlertTitle>{{ $t('永久删除受保护') }}</AlertTitle>
          <AlertDescription class="flex flex-col gap-2">
            <span>{{ $t('以下永久历史或业务引用仍然存在，因此不能物理删除该竞赛。软删除与恢复不影响这些记录。') }}</span>
            <span v-if="hardDeletePreview.references?.some(reference => reference.code === 'HistoricalEvent')" class="font-medium">
              {{ $t('比赛事件永久保留，不能清理或修改。') }}
            </span>
            <span class="flex flex-wrap gap-2">
              <Badge v-for="reference in hardDeletePreview.references" :key="reference.code" variant="outline">
                {{ hardDeleteReferenceLabel(reference.code) }} · {{ reference.count ?? 0 }}
              </Badge>
            </span>
            <span v-if="isAdministrator" class="text-destructive">
              {{ hardDeletePreview.canForceDelete
                ? $t('平台管理员可使用强制级联删除。该操作会永久移除比赛作用域数据，只保留一条平台审计记录。')
                : $t('强制级联删除当前受阻：请先结束比赛并清理全部活动运行环境资源。') }}
            </span>
          </AlertDescription>
        </Alert>
        <Alert v-else-if="canManagePermissions && hardDeletePreview?.canHardDelete" class="w-full">
          <AlertDescription>{{ $t('影响检查通过：该竞赛没有永久历史或业务引用，可直接物理删除，无需先软删除。') }}</AlertDescription>
        </Alert>
      </CardContent>
    </Card>

    <AlertDialog :open="confirmTarget !== null" @update:open="(v) => { if (!v) confirmTarget = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTarget?.confirm?.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmTarget?.confirm?.description }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="pendingAction !== null">{{ $t('取消') }}</AlertDialogCancel>
          <Button
            type="button"
            :variant="confirmTarget?.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="confirmTarget && execute(confirmTarget)"
          >
            <Spinner v-if="pendingAction !== null" data-icon="inline-start" />
            {{ pendingAction !== null ? $t('处理中') : $t('确认') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="forceDeleteOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('强制级联删除竞赛') }}</DialogTitle>
          <DialogDescription>
            {{ $t('此操作不可恢复，将永久删除比赛、队伍、题目实例、提交、事件、通知、运行时和导出等比赛作用域数据。平台审计会保留操作者、原因和时间。') }}
          </DialogDescription>
        </DialogHeader>
        <Alert v-if="forceDeleteError" variant="destructive">
          <AlertDescription>{{ forceDeleteError }}</AlertDescription>
        </Alert>
        <FieldGroup>
          <Field>
            <FieldLabel for="force-delete-title">{{ $t('输入完整竞赛标题以确认') }}</FieldLabel>
            <Input
              id="force-delete-title"
              v-model="forceDeleteTitle"
              autocomplete="off"
              :placeholder="competition.title"
              :disabled="forceDeleting"
            />
            <FieldError v-if="forceDeleteTitle && forceDeleteTitle !== competition.title">
              {{ $t('竞赛标题必须完全一致') }}
            </FieldError>
          </Field>
          <Field>
            <FieldLabel for="force-delete-reason">{{ $t('删除原因') }}</FieldLabel>
            <Textarea
              id="force-delete-reason"
              v-model="forceDeleteReason"
              rows="4"
              maxlength="500"
              :disabled="forceDeleting"
            />
            <FieldDescription>{{ $t('至少 8 个字符；该原因会写入平台审计日志。') }}</FieldDescription>
            <FieldError v-if="forceDeleteReason && forceDeleteReason.trim().length < 8">
              {{ $t('删除原因至少需要 8 个字符') }}
            </FieldError>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="forceDeleting" @click="forceDeleteOpen = false">{{ $t('取消') }}</Button>
          <Button variant="destructive" :disabled="forceDeleting || !forceDeleteValid" @click="forceDelete">
            <Spinner v-if="forceDeleting" data-icon="inline-start" />
            {{ forceDeleting ? $t('正在永久删除') : $t('确认强制删除') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteConfirm !== null" @update:open="(v) => { if (!v) deleteConfirm = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ deleteConfirm === 'hard' ? $t('彻底删除竞赛') : $t('删除竞赛') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ deleteConfirm === 'hard'
              ? $t('影响检查已确认该空竞赛没有永久历史或业务引用。物理删除不可恢复，确认继续？')
              : $t('删除后竞赛将对选手不可见,可稍后恢复。确认删除?') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deleting || hardDeleting">{{ $t('取消') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="deleting || hardDeleting"
            @click="submitDelete"
          >
            <Spinner v-if="deleting || hardDeleting" data-icon="inline-start" />
            {{ deleting || hardDeleting ? $t('处理中') : $t('确认删除') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
