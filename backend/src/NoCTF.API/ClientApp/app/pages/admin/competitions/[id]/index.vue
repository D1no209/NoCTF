<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminDeleteCompetition,
  adminFinishCompetition,
  adminGenerateMissingFlags,
  adminHardDeleteCompetition,
  adminMakeCompetitionVisible,
  adminPauseCompetition,
  adminPublishCompetition,
  adminRestoreCompetition,
  adminResumeCompetition,
  adminStartCompetition,
  adminValidateCompetitionStart,
} from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse } from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, competition, canWrite, canManagePermissions, refresh } = useCompetitionAdmin()

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
    label: '对外可见',
    visible: status.value === 'Draft',
    run: () => adminMakeCompetitionVisible({ path: { competitionId } }),
  },
  {
    key: 'publish',
    label: '发布竞赛',
    visible: status.value === 'Visible',
    run: () => adminPublishCompetition({ path: { competitionId } }),
  },
  {
    key: 'start',
    label: '开始比赛',
    visible: status.value === 'Published',
    confirm: { title: '开始比赛', description: '将立即开始比赛并为队伍预置运行时实例。确认继续?' },
    run: () => adminStartCompetition({ path: { competitionId } }),
  },
  {
    key: 'pause',
    label: '暂停比赛',
    visible: status.value === 'Running',
    run: () => adminPauseCompetition({ path: { competitionId } }),
  },
  {
    key: 'resume',
    label: '恢复比赛',
    visible: status.value === 'Paused',
    run: () => adminResumeCompetition({ path: { competitionId } }),
  },
  {
    key: 'finish',
    label: '结束比赛',
    visible: status.value === 'Published' || status.value === 'Running' || status.value === 'Paused',
    destructive: true,
    confirm: { title: '结束比赛', description: '结束比赛不可撤销,将清理全部运行时实例。确认结束?' },
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
    toast.success(`${action.label}成功`)
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
    if (validationErrors.value.length === 0) toast.success('启动前检查通过')
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
    if (generateFailures.value.length === 0) toast.success('缺失 Flag 已全部生成')
    else toast.warning(`生成完成,${generateFailures.value.length} 项失败`)
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
const deleteConfirm = ref<'soft' | 'hard' | null>(null)

async function softDelete() {
  deleting.value = true
  try {
    const { error } = await adminDeleteCompetition({ path: { competitionId } })
    if (error) throw error
    toast.success('竞赛已删除')
    await refresh()
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
    toast.success('竞赛已恢复')
    await refresh()
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
    toast.success('竞赛已彻底删除')
    await navigateTo('/admin/competitions')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    hardDeleting.value = false
    deleteConfirm.value = null
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
        <CardTitle>生命周期</CardTitle>
        <CardDescription>
          {{ adminFormatDateTime(competition.startTime) }} ~ {{ adminFormatDateTime(competition.endTime) }}
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-6">
        <div class="flex flex-wrap items-center gap-2">
          <template v-for="(step, i) in steps" :key="step.value">
            <Badge :variant="status === step.value || (step.value === 'Running' && status === 'Paused') ? 'default' : 'outline'">
              {{ step.value === 'Running' && status === 'Paused' ? '已暂停' : step.label }}
            </Badge>
            <span v-if="i < steps.length - 1" class="text-muted-foreground">→</span>
          </template>
        </div>

        <Alert v-if="actionError" variant="destructive">
          <AlertDescription>{{ actionError }}</AlertDescription>
        </Alert>

        <div v-if="canWrite && !isDeleted" class="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" :disabled="validating" @click="validateStart">
            <Spinner v-if="validating" data-icon="inline-start" />
            启动前检查
          </Button>
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
        <p v-else class="text-sm text-muted-foreground">当前角色为只读,无法执行生命周期操作</p>

        <div v-if="validationErrors && validationErrors.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(ve, i) in validationErrors" :key="i" variant="destructive">
            <AlertDescription>
              <span class="font-mono text-xs">{{ ve.code }}</span>
              — {{ ve.message }}
              <NuxtLink
                v-if="ve.competitionChallengeId"
                class="underline"
                :to="`/admin/competitions/${competitionId}/challenges/${ve.competitionChallengeId}`"
              >
                查看题目
              </NuxtLink>
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite && !isDeleted">
      <CardHeader>
        <CardTitle>Flag 生成</CardTitle>
        <CardDescription>为动态 Flag 题目批量生成缺失的队伍 Flag(幂等)</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <div>
          <Button variant="outline" size="sm" :disabled="generating" @click="generateMissingFlags">
            <Spinner v-if="generating" data-icon="inline-start" />
            生成缺失 Flag
          </Button>
        </div>
        <div v-if="generateFailures && generateFailures.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(f, i) in generateFailures" :key="i" variant="destructive">
            <AlertDescription>
              题目 {{ f.competitionChallengeId }} / 队伍 {{ f.teamId }}:{{ f.description ?? f.code }}
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite">
      <CardHeader>
        <CardTitle class="text-destructive">危险区</CardTitle>
        <CardDescription v-if="isDeleted">
          此竞赛已于 {{ adminFormatDateTime(competition.deletedAt) }} 删除。恢复不会丢失历史数据；彻底删除受历史引用保护。
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap items-center gap-2">
        <Button v-if="!isDeleted" variant="outline" size="sm" :disabled="deleting" @click="deleteConfirm = 'soft'">
          删除竞赛
        </Button>
        <template v-if="isDeleted && canManagePermissions">
          <Button variant="outline" size="sm" :disabled="restoring" @click="restore">
            <Spinner v-if="restoring" data-icon="inline-start" />
            恢复已删除竞赛
          </Button>
          <Button variant="destructive" size="sm" :disabled="hardDeleting" @click="deleteConfirm = 'hard'">
            彻底删除
          </Button>
        </template>
      </CardContent>
    </Card>

    <AlertDialog :open="confirmTarget !== null" @update:open="(v) => { if (!v) confirmTarget = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTarget?.confirm?.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmTarget?.confirm?.description }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="pendingAction !== null">取消</AlertDialogCancel>
          <Button
            type="button"
            :variant="confirmTarget?.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="confirmTarget && execute(confirmTarget)"
          >
            <Spinner v-if="pendingAction !== null" data-icon="inline-start" />
            {{ pendingAction !== null ? '处理中' : '确认' }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog :open="deleteConfirm !== null" @update:open="(v) => { if (!v) deleteConfirm = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ deleteConfirm === 'hard' ? '彻底删除竞赛' : '删除竞赛' }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ deleteConfirm === 'hard'
              ? '彻底删除不可恢复,仅适用于没有任何持久数据的空竞赛。确认继续?'
              : '删除后竞赛将对选手不可见,可稍后恢复。确认删除?' }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deleting || hardDeleting">取消</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="deleting || hardDeleting"
            @click="submitDelete"
          >
            <Spinner v-if="deleting || hardDeleting" data-icon="inline-start" />
            {{ deleting || hardDeleting ? '处理中' : '确认删除' }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
