<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { CheckCircle2, Copy, Loader2, RotateCcw, Server, Square, Trash2 } from 'lucide-vue-next'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { runtimeStatusVariant } from '@/lib/statusTones'

interface Challenge {
  id: string
  title: string
}

interface PenetrationFlag {
  id: string
  name: string
  stage: number
  score: number
  solved?: boolean
  solvedAt?: string | null
  hintAfterSolved?: string | null
  visible?: boolean
}

interface PenetrationInstance {
  id?: string | null
  status: string
  entryUrl?: string | null
  resetCount: number
  resetLimit: number
  expiresAt?: string | null
  cooldownUntil?: string | null
  lastError?: string | null
}

interface PenetrationDetail {
  authorizationScope?: string
  topology?: {
    flags?: PenetrationFlag[]
  } | null
  instance: PenetrationInstance
  totalStageCount: number
  solvedStageCount: number
  totalScore: number
}

interface SubmitResult {
  correct: boolean
  alreadySolved?: boolean
  result?: string
  data?: unknown
}

const props = defineProps<{
  competitionId: string
  challenge: Challenge
  canSubmitFlag?: boolean
}>()

const emit = defineEmits<{
  solved: []
}>()

const { t } = useI18n()
const qc = useQueryClient()
const flagInput = ref('')
const submitMessage = ref('')
const destroyArmed = ref(false)

const detailQuery = useQuery({
  queryKey: computed(() => queryKeys.penetrationDetail(props.competitionId, props.challenge.id)),
  queryFn: () =>
    competitionApi.penetrationDetail<PenetrationDetail>(props.competitionId, props.challenge.id),
  refetchInterval: 10_000,
})

const detail = computed(() => detailQuery.data.value)
const instance = computed(() => detail.value?.instance ?? null)
const flags = computed(() =>
  (detail.value?.topology?.flags ?? []).filter((flag) => flag.visible !== false),
)
const running = computed(() => instance.value?.status === 'Running')
const canOperate = computed(() => props.canSubmitFlag !== false)
const busy = computed(() =>
  ['Starting', 'Stopping', 'Resetting', 'Destroying'].includes(instance.value?.status ?? ''),
)

function invalidate() {
  qc.invalidateQueries({
    queryKey: queryKeys.penetrationDetail(props.competitionId, props.challenge.id),
  })
  qc.invalidateQueries({ queryKey: queryKeys.submissions(props.competitionId) })
  qc.invalidateQueries({ queryKey: queryKeys.challenges(props.competitionId) })
  qc.invalidateQueries({ queryKey: queryKeys.leaderboard(props.competitionId) })
}

const startMutation = useMutation({
  mutationFn: () => competitionApi.penetrationStart(props.competitionId, props.challenge.id),
  onSuccess: () => {
    toast.success(t('penetration.instanceStarted'))
    invalidate()
  },
  onError: () => toast.error(t('penetration.instanceActionFailed')),
})

const stopMutation = useMutation({
  mutationFn: () => competitionApi.penetrationStop(props.competitionId, props.challenge.id),
  onSuccess: invalidate,
  onError: () => toast.error(t('penetration.instanceActionFailed')),
})

const resetMutation = useMutation({
  mutationFn: () => competitionApi.penetrationReset(props.competitionId, props.challenge.id),
  onSuccess: invalidate,
  onError: () => toast.error(t('penetration.instanceActionFailed')),
})

const destroyMutation = useMutation({
  mutationFn: () => competitionApi.penetrationDestroy(props.competitionId, props.challenge.id),
  onSuccess: () => {
    destroyArmed.value = false
    invalidate()
  },
  onError: () => toast.error(t('penetration.instanceActionFailed')),
})

const submitMutation = useMutation({
  mutationFn: () =>
    competitionApi.submitPenetrationFlag<SubmitResult>(
      props.competitionId,
      props.challenge.id,
      flagInput.value.trim(),
    ),
  onSuccess: (data) => {
    if (data.correct) {
      submitMessage.value = data.alreadySolved
        ? t('challenges.alreadySolved')
        : t('challenges.correctFlag')
      flagInput.value = ''
      emit('solved')
      invalidate()
      return
    }
    submitMessage.value =
      data.result === 'flag_rate_limited'
        ? t('penetration.flagRateLimited')
        : t('challenges.incorrectFlag')
  },
  onError: () => {
    submitMessage.value = t('challenges.submissionFailed')
  },
})

async function copyEntry() {
  if (!instance.value?.entryUrl) return
  try {
    await navigator.clipboard.writeText(instance.value.entryUrl)
    toast.success(t('challenges.addressCopied'))
  } catch {
    toast.error(t('common.copyFailed'))
  }
}

function requestDestroy() {
  if (!destroyArmed.value) {
    destroyArmed.value = true
    return
  }
  destroyMutation.mutate()
}

watch(
  () => instance.value?.id,
  () => {
    destroyArmed.value = false
  },
)
</script>

<template>
  <div class="space-y-4">
    <Alert>
      {{ detail?.authorizationScope ?? t('penetration.scopeDefault') }}
    </Alert>

    <div class="rounded-lg border bg-muted/20 p-3">
      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="space-y-1">
          <div class="flex items-center gap-2 text-sm font-semibold">
            <Server class="size-4 text-primary" />
            <span>{{ t('penetration.instance') }}</span>
            <Badge :variant="runtimeStatusVariant(instance?.status)">{{ instance?.status ?? 'None' }}</Badge>
          </div>
          <div
            v-if="running && instance?.entryUrl"
            class="flex flex-wrap items-center gap-2 text-sm"
          >
            <code class="break-all rounded bg-background px-2 py-1">{{ instance.entryUrl }}</code>
            <Button type="button" variant="ghost" size="icon-sm" @click="copyEntry">
              <Copy class="size-4" />
            </Button>
          </div>
          <p v-else-if="instance?.lastError" class="text-xs text-danger">
            {{ instance.lastError }}
          </p>
        </div>
        <div class="flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            :disabled="!canOperate || busy || running || startMutation.isPending.value"
            @click="startMutation.mutate()"
          >
            <Loader2 v-if="startMutation.isPending.value" class="size-4 animate-spin" />
            <Server v-else class="size-4" />
            {{ t('penetration.start') }}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            :disabled="!canOperate || busy || !running"
            @click="stopMutation.mutate()"
          >
            <Square class="size-4" />
            {{ t('penetration.stop') }}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            :disabled="!canOperate || busy || !instance?.id"
            @click="resetMutation.mutate()"
          >
            <RotateCcw class="size-4" />
            {{
              t('penetration.resetCount', {
                count: instance?.resetCount ?? 0,
                limit: instance?.resetLimit ?? 0,
              })
            }}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="destructive"
            :disabled="!canOperate || busy || !instance?.id"
            @click="requestDestroy"
          >
            <Trash2 class="size-4" />
            {{ destroyArmed ? t('common.confirm') : t('penetration.destroy') }}
          </Button>
        </div>
      </div>
    </div>

    <div class="rounded-lg border">
      <div class="border-b px-3 py-2 text-sm font-semibold">
        {{ t('penetration.stages') }} · {{ detail?.solvedStageCount ?? 0 }} /
        {{ detail?.totalStageCount ?? flags.length }}
      </div>
      <div class="divide-y">
        <div
          v-for="flag in flags"
          :key="flag.id"
          class="flex items-center justify-between gap-3 px-3 py-2 text-sm"
        >
          <div>
            <div class="font-medium">{{ flag.stage }}. {{ flag.name }}</div>
            <p v-if="flag.solved && flag.hintAfterSolved" class="text-xs text-muted-foreground">
              {{ flag.hintAfterSolved }}
            </p>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-xs text-muted-foreground">{{ flag.score }} {{ t('nav.score') }}</span>
            <Badge :variant="flag.solved ? 'success' : 'warning'">
              <CheckCircle2 v-if="flag.solved" class="mr-1 size-3" />
              {{ flag.solved ? t('challenges.solved') : t('common.pending') }}
            </Badge>
          </div>
        </div>
      </div>
    </div>

    <div class="flex gap-2">
      <Input
        v-model="flagInput"
        :disabled="submitMutation.isPending.value || !canOperate"
        :placeholder="t('challenges.flagPlaceholder')"
        @keydown.enter="submitMutation.mutate()"
      />
      <Button
        :disabled="submitMutation.isPending.value || !flagInput.trim() || !canOperate"
        @click="submitMutation.mutate()"
      >
        <Loader2 v-if="submitMutation.isPending.value" class="size-4 animate-spin" />
        {{ t('common.submit') }}
      </Button>
    </div>
    <Alert v-if="submitMessage">
      {{ submitMessage }}
    </Alert>
  </div>
</template>
