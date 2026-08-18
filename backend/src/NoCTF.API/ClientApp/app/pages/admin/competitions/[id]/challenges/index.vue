<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminChallengeBankListTemplates,
  adminCreateCompetitionChallenge,
  adminDeleteCompetitionChallenge,
  adminListCompetitionChallenges,
  adminRestoreCompetitionChallenge,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { competitionChallengeConflictMessage } from '~/lib/competition-challenge-conflict'

definePageMeta({ middleware: 'auth' })

const { competitionId, competition, canWrite } = useCompetitionAdmin()

const items = ref<NoCtfapiEndpointsChallengesChallengeResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const includeDeleted = ref(false)
const pendingId = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  const { data, error: e } = await adminListCompetitionChallenges({
    path: { competitionId },
    query: { includeDeleted: includeDeleted.value },
  })
  if (e) error.value = parseApiError(e).message
  else items.value = [...(data?.items ?? [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
  loading.value = false
}

watch(includeDeleted, load)
onMounted(load)

// ---- Add from challenge bank ----
const addOpen = ref(false)
const templates = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse[]>([])
const templatesLoading = ref(false)
const selectedTemplateId = ref<string>('')
const newCustomTitle = ref('')
const newBaseScore = ref(100)
const newOrder = ref(0)
const adding = ref(false)
const addError = ref<string | null>(null)

const modeTemplates = computed(() =>
  templates.value.filter(t => t.mode === competition.value?.mode && !t.deletedAt),
)
const isAwdp = computed(() => competition.value?.mode === 'Awdp')

async function openAdd() {
  addOpen.value = true
  addError.value = null
  selectedTemplateId.value = ''
  newCustomTitle.value = ''
  newOrder.value = (items.value.filter(i => !i.deletedAt).map(i => i.order ?? 0).reduce((m, o) => Math.max(m, o), 0) || 0) + 1
  templatesLoading.value = true
  const { data, error: e } = await adminChallengeBankListTemplates({ query: { includeDeleted: false } })
  if (e) addError.value = parseApiError(e).message
  else templates.value = data?.items ?? []
  templatesLoading.value = false
}

async function addChallenge() {
  if (!selectedTemplateId.value) {
    addError.value = translate('请选择题库模板')
    return
  }
  adding.value = true
  addError.value = null
  try {
    const { error } = await adminCreateCompetitionChallenge({
      path: { competitionId },
      body: {
        challengeId: selectedTemplateId.value,
        customTitle: newCustomTitle.value.trim() || null,
        baseScore: isAwdp.value ? 0 : newBaseScore.value,
        order: newOrder.value,
      },
    })
    if (error) {
      addError.value = competitionChallengeConflictMessage(error) ?? parseApiError(error).message
      return
    }
    toast.success(translate("题目已添加"))
    addOpen.value = false
    await load()
  }
  catch (e) {
    addError.value = competitionChallengeConflictMessage(e) ?? parseApiError(e).message
  }
  finally {
    adding.value = false
  }
}

// ---- Delete / restore ----
const deleteTarget = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)
const deletePending = ref(false)
const deleteError = ref<string | null>(null)

function closeDeleteDialog(open: boolean) {
  if (!open && !deletePending.value) {
    deleteTarget.value = null
    deleteError.value = null
  }
}

function beginDeleteChallenge(c: NoCtfapiEndpointsChallengesChallengeResponse) {
  deleteTarget.value = c
  deleteError.value = null
}

async function removeChallenge() {
  const target = deleteTarget.value
  if (!target?.id || deletePending.value) return
  deletePending.value = true
  deleteError.value = null
  pendingId.value = target.id
  try {
    const { error } = await adminDeleteCompetitionChallenge({
      path: { competitionId, competitionChallengeId: target.id },
      query: { expectedRevision: target.revision ?? 0 },
    })
    if (error) throw error
    toast.success(translate("题目已删除"))
    deleteTarget.value = null
    deleteError.value = null
    await load()
  }
  catch (e) {
    deleteError.value = parseApiError(e).message
    toast.error(deleteError.value)
  }
  finally {
    deletePending.value = false
    pendingId.value = null
  }
}

async function restoreChallenge(c: NoCtfapiEndpointsChallengesChallengeResponse) {
  if (!c.id) return
  pendingId.value = c.id
  try {
    const { error } = await adminRestoreCompetitionChallenge({
      path: { competitionId, competitionChallengeId: c.id },
      query: { expectedRevision: c.revision ?? 0 },
    })
    if (error) throw error
    toast.success(translate("题目已恢复"))
    await load()
  }
  catch (e) {
    toastWriteError(e, load)
  }
  finally {
    pendingId.value = null
  }
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Checkbox id="show-deleted" v-model="includeDeleted" />
        <label for="show-deleted" class="text-sm text-muted-foreground">{{ $t('显示已删除') }}</label>
      </div>
      <Button v-if="canWrite" size="sm" @click="openAdd">
        <Plus data-icon="inline-start" /> {{ $t('从题库添加') }} </Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-48 w-full" />

    <Empty v-else-if="items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('暂无题目') }}</EmptyTitle>
        <EmptyDescription>{{ $t('从题库中添加题目后开始配置竞赛') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead class="w-16">{{ $t('顺序') }}</TableHead>
          <TableHead>{{ $t('标题') }}</TableHead>
          <TableHead>{{ $t('方向') }}</TableHead>
          <TableHead v-if="!isAwdp" class="w-24">{{ $t('基础分') }}</TableHead>
          <TableHead class="w-28">{{ $t('状态') }}</TableHead>
          <TableHead v-if="canWrite" class="w-40 text-right">{{ $t('操作') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="c in items" :key="c.id" :class="{ 'opacity-60': c.deletedAt }">
          <TableCell class="font-mono tabular-nums">{{ c.order }}</TableCell>
          <TableCell>
            <NuxtLink
              v-if="!c.deletedAt"
              class="font-medium underline-offset-4 hover:underline"
              :to="`/admin/competitions/${competitionId}/challenges/${c.id}`"
            >
              {{ c.title }}
            </NuxtLink>
            <span v-else class="font-medium">{{ c.title }}</span>
          </TableCell>
          <TableCell>
            <Badge variant="outline" :class="directionBadgeClass(c.direction)">
              {{ c.direction }}
            </Badge>
          </TableCell>
          <TableCell v-if="!isAwdp" class="font-mono tabular-nums">{{ c.baseScore ?? '-' }}</TableCell>
          <TableCell>
            <Badge v-if="c.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
            <Badge v-else :variant="c.isPublished ? 'default' : 'outline'">
              {{ c.isPublished ? $t('已发布') : $t('未发布') }}
            </Badge>
          </TableCell>
          <TableCell v-if="canWrite" class="text-right">
            <div class="flex justify-end gap-1">
              <Button
                v-if="c.deletedAt"
                variant="outline"
                size="sm"
                :disabled="pendingId === c.id"
                @click="restoreChallenge(c)"
              >
                <Spinner v-if="pendingId === c.id" data-icon="inline-start" /> {{ $t('恢复') }} </Button>
              <Button
                v-else
                variant="ghost"
                size="sm"
                :disabled="pendingId === c.id"
                @click="beginDeleteChallenge(c)"
              > {{ $t('删除') }} </Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <Dialog v-model:open="addOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('从题库添加题目') }}</DialogTitle>
          <DialogDescription>{{ $t('选择与本竞赛模式匹配的题库模板,实例化为竞赛题目') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="addError" variant="destructive">
            <AlertDescription>{{ addError }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="tpl">{{ $t('题库模板') }}</FieldLabel>
            <Skeleton v-if="templatesLoading" class="h-9 w-full" />
            <Select v-else id="tpl" v-model="selectedTemplateId">
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="$t('选择模板')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="t in modeTemplates" :key="t.id" :value="t.id!">
                    {{ t.title }}({{ t.direction }})
                  </SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <FieldDescription v-if="!templatesLoading && modeTemplates.length === 0">
              {{ $t('题库中没有 {mode} 模式的可用模板', { mode: enumLabel(GameModeLabel, competition?.mode) }) }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="new-title">{{ $t('比赛题目名称') }}</FieldLabel>
            <Input
              id="new-title"
              v-model="newCustomTitle"
              maxlength="160"
              :placeholder="$t('留空时使用题库模板标题')"
            />
            <FieldDescription>{{ $t('只修改本场比赛中的展示名称,不会更改题库模板') }}</FieldDescription>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field v-if="!isAwdp">
              <FieldLabel for="new-score">{{ $t('基础分') }}</FieldLabel>
              <Input id="new-score" v-model.number="newBaseScore" type="number" min="0" />
            </Field>
            <Field>
              <FieldLabel for="new-order">{{ $t('顺序') }}</FieldLabel>
              <Input id="new-order" v-model.number="newOrder" type="number" min="0" />
            </Field>
          </div>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="addOpen = false">{{ $t('取消') }}</Button>
          <Button :disabled="adding || !selectedTemplateId" @click="addChallenge">
            <Spinner v-if="adding" data-icon="inline-start" /> {{ $t('添加') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteTarget !== null" @update:open="closeDeleteDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('删除题目') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('删除「{title}」后选手将无法看到该题，可稍后恢复。确认删除？', { title: deleteTarget?.title ?? '-' }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="deleteError" variant="destructive">
          <AlertDescription>{{ deleteError }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deletePending">{{ $t('取消') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="deletePending" @click="removeChallenge">
            <Spinner v-if="deletePending" data-icon="inline-start" />
            {{ deletePending ? $t('处理中') : $t('确认删除') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
