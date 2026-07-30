<script setup lang="ts">
import type { CompetitionChallenge } from '@/api/noctf'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  ArchiveRestore,
  Check,
  Copy,
  Edit3,
  Loader2,
  Plus,
  RefreshCw,
  Trash2,
} from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { ApiError, competitionChallengeAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { normalizeDirection } from '@/lib/challengeDirections'
import { isDeletedCompetitionChallenge } from './competitionChallengeLifecycle'

type LifecycleAction = 'delete' | 'restore'

const props = defineProps<{
  competitionId: string
}>()

const { t } = useI18n()
const queryClient = useQueryClient()
const allDirections = '__all__'
const direction = ref(allDirections)
const includeDeleted = ref(false)
const copiedId = ref('')
const lifecycleDialog = ref(false)
const lifecycleAction = ref<LifecycleAction | null>(null)
const selectedChallenge = ref<CompetitionChallenge | null>(null)

const challengeQueryKey = computed(() => [
  ...queryKeys.adminCompetitionChallenges(props.competitionId),
  { includeDeleted: includeDeleted.value },
])

const {
  data: challenges,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: challengeQueryKey,
  queryFn: () => competitionChallengeAdminApi.list(props.competitionId, includeDeleted.value),
})

const directions = computed(() =>
  [...new Set((challenges.value ?? []).map(challenge => normalizeDirection(challenge.direction)))].sort(),
)
const filteredChallenges = computed(() =>
  direction.value === allDirections
    ? challenges.value ?? []
    : (challenges.value ?? []).filter(
        challenge => normalizeDirection(challenge.direction) === direction.value,
      ),
)

const lifecycleMutation = useMutation({
  mutationFn: async ({
    action,
    competitionChallengeId,
  }: {
    action: LifecycleAction
    competitionChallengeId: string
  }) => {
    if (action === 'restore')
      await competitionChallengeAdminApi.restore(props.competitionId, competitionChallengeId)
    else
      await competitionChallengeAdminApi.delete(props.competitionId, competitionChallengeId)
  },
  onSuccess: (_, variables) => {
    lifecycleDialog.value = false
    selectedChallenge.value = null
    lifecycleAction.value = null
    void queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionChallenges(props.competitionId),
    })
    toast.success(
      variables.action === 'restore'
        ? t('admin.competitionDetail.restoreChallengeSuccess')
        : t('admin.competitionDetail.removeChallengeSuccess'),
    )
  },
  onError: async (error, variables) => {
    const staleLifecycleState
      = error instanceof ApiError && (error.status === 404 || error.status === 409)
    if (staleLifecycleState) {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.adminCompetitionChallenges(props.competitionId),
      })
    }
    toast.error(
      staleLifecycleState
        ? t('admin.competitionDetail.challengeLifecycleConflict')
        : variables.action === 'restore'
          ? t('admin.competitionDetail.restoreChallengeError')
          : t('admin.competitionDetail.removeChallengeError'),
    )
  },
})

function displayTitle(challenge: CompetitionChallenge) {
  return challenge.title?.trim() || challenge.id || t('admin.challenges.unknownTemplate')
}

async function copyStableId(id?: string) {
  if (!id)
    return

  try {
    await navigator.clipboard.writeText(id)
    copiedId.value = id
    toast.success(t('admin.competitionDetail.challengeIdCopied'))
    window.setTimeout(() => {
      if (copiedId.value === id)
        copiedId.value = ''
    }, 1500)
  }
  catch {
    toast.error(t('admin.competitionDetail.challengeIdCopyError'))
  }
}

function openLifecycleDialog(challenge: CompetitionChallenge, action: LifecycleAction) {
  if (!challenge.id || (action === 'restore') !== isDeletedCompetitionChallenge(challenge))
    return

  selectedChallenge.value = challenge
  lifecycleAction.value = action
  lifecycleDialog.value = true
}

function confirmLifecycleAction() {
  const competitionChallengeId = selectedChallenge.value?.id
  const action = lifecycleAction.value
  if (!competitionChallengeId || !action)
    return

  lifecycleMutation.mutate({ action, competitionChallengeId })
}

function onLifecycleDialogChange(open: boolean) {
  if (!open && lifecycleMutation.isPending.value)
    return

  lifecycleDialog.value = open
  if (!open) {
    selectedChallenge.value = null
    lifecycleAction.value = null
  }
}
</script>

<template>
  <section class="space-y-5">
    <div class="flex flex-col gap-4 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h3 class="text-lg font-semibold">
          {{ t('admin.competitionDetail.competitionChallenges') }}
        </h3>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.competitionDetail.challengeCardsDescription') }}
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button
          variant="outline"
          :aria-pressed="includeDeleted"
          @click="includeDeleted = !includeDeleted"
        >
          <ArchiveRestore class="size-4" />
          {{
            includeDeleted
              ? t('admin.competitionDetail.hideDeletedChallenges')
              : t('admin.competitionDetail.includeDeletedChallenges')
          }}
        </Button>
        <Button as-child>
          <RouterLink
            :to="{ name: 'admin-competition-challenge-create', params: { id: competitionId } }"
          >
            <Plus class="size-4" />
            {{ t('admin.competitionDetail.createCompetitionChallenge') }}
          </RouterLink>
        </Button>
      </div>
    </div>

    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <Select v-model="direction">
        <SelectTrigger class="w-full sm:w-64">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="allDirections">
            {{ t('admin.challenges.allDirections') }}
          </SelectItem>
          <SelectItem v-for="item in directions" :key="item" :value="item">
            {{ item }}
          </SelectItem>
        </SelectContent>
      </Select>
      <p v-if="!isLoading" class="text-xs text-muted-foreground">
        {{
          t('admin.competitionDetail.challengeResultCount', {
            count: filteredChallenges.length,
          })
        }}
      </p>
    </div>

    <div
      v-if="isLoading"
      class="flex min-h-56 items-center justify-center text-sm text-muted-foreground"
    >
      <Loader2 class="mr-2 size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>

    <Card
      v-else-if="isError"
      class="flex min-h-56 flex-col items-center justify-center border-dashed p-8 text-center"
    >
      <p class="font-medium">
        {{ t('errors.loadCompetitionChallenges') }}
      </p>
      <Button variant="outline" class="mt-4" @click="refetch()">
        <RefreshCw class="size-4" />
        {{ t('common.retry') }}
      </Button>
    </Card>

    <div
      v-else-if="filteredChallenges.length"
      class="grid gap-4 md:grid-cols-2 xl:grid-cols-3"
    >
      <Card
        v-for="challenge in filteredChallenges"
        :key="challenge.id"
        class="group p-0"
        :class="isDeletedCompetitionChallenge(challenge) ? 'bg-muted/20' : ''"
      >
        <CardContent class="flex h-full min-h-64 flex-col p-5">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <h4
                class="truncate font-semibold"
                :class="
                  isDeletedCompetitionChallenge(challenge)
                    ? 'text-muted-foreground line-through'
                    : ''
                "
                :title="displayTitle(challenge)"
              >
                {{ displayTitle(challenge) }}
              </h4>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ normalizeDirection(challenge.direction) }}
              </p>
            </div>
            <Badge
              :variant="isDeletedCompetitionChallenge(challenge) ? 'secondary' : 'outline'"
            >
              {{
                isDeletedCompetitionChallenge(challenge)
                  ? t('admin.competitionDetail.challengeDeleted')
                  : challenge.isPublished
                    ? t('admin.competitionDetail.challengePublished')
                    : t('admin.competitionDetail.challengeDraft')
              }}
            </Badge>
          </div>

          <div class="mt-4 border bg-muted/30 p-3">
            <div class="flex items-start gap-2">
              <code class="min-w-0 flex-1 break-all font-mono text-xs">
                {{ challenge.id }}
              </code>
              <Button
                variant="ghost"
                size="icon"
                class="size-7 shrink-0"
                :title="t('admin.competitionDetail.copyChallengeId')"
                @click="copyStableId(challenge.id)"
              >
                <Check v-if="copiedId === challenge.id" class="size-3.5" />
                <Copy v-else class="size-3.5" />
              </Button>
            </div>
            <p class="mt-2 break-all font-mono text-[11px] text-muted-foreground">
              {{ t('admin.competitionDetail.templateId') }}: {{ challenge.challengeId }}
            </p>
          </div>

          <dl class="mt-4 grid grid-cols-3 gap-3 text-sm">
            <div class="border-l-2 border-primary/30 pl-3">
              <dt class="text-xs text-muted-foreground">
                {{ t('admin.competitionDetail.baseScore') }}
              </dt>
              <dd class="mt-1 font-mono">
                {{ challenge.baseScore ?? '—' }}
              </dd>
            </div>
            <div class="border-l-2 border-primary/30 pl-3">
              <dt class="text-xs text-muted-foreground">
                {{ t('admin.competitionDetail.challengeOrder') }}
              </dt>
              <dd class="mt-1 font-mono">
                {{ challenge.order ?? '—' }}
              </dd>
            </div>
            <div class="border-l-2 border-primary/30 pl-3">
              <dt class="text-xs text-muted-foreground">
                {{ t('admin.competitionDetail.challengeRevision') }}
              </dt>
              <dd class="mt-1 font-mono">
                {{ challenge.revision ?? '—' }}
              </dd>
            </div>
          </dl>

          <div class="mt-auto flex items-center justify-end gap-2 border-t pt-4">
            <Button
              v-if="isDeletedCompetitionChallenge(challenge)"
              variant="outline"
              size="sm"
              :disabled="lifecycleMutation.isPending.value"
              @click="openLifecycleDialog(challenge, 'restore')"
            >
              <ArchiveRestore class="size-4" />
              {{ t('admin.competitionDetail.restoreChallenge') }}
            </Button>
            <template v-else>
              <Button variant="outline" size="sm" as-child>
                <RouterLink
                  :to="{
                    name: 'admin-competition-challenge-edit',
                    params: {
                      id: competitionId,
                      competitionChallengeId: challenge.id,
                    },
                  }"
                >
                  <Edit3 class="size-4" />
                  {{ t('common.edit') }}
                </RouterLink>
              </Button>
              <Button
                variant="ghost"
                size="icon"
                class="text-destructive"
                :disabled="lifecycleMutation.isPending.value"
                :title="t('common.delete')"
                @click="openLifecycleDialog(challenge, 'delete')"
              >
                <Trash2 class="size-4" />
              </Button>
            </template>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card
      v-else
      class="flex min-h-56 flex-col items-center justify-center border-dashed p-8 text-center"
    >
      <p class="font-medium">
        {{ t('admin.competitionDetail.noDeployedChallenges') }}
      </p>
      <p class="mt-1 text-sm text-muted-foreground">
        {{ t('admin.competitionDetail.noChallengeFilterResults') }}
      </p>
    </Card>

    <Dialog :open="lifecycleDialog" @update:open="onLifecycleDialogChange">
      <DialogContent class="sm:max-w-[480px]">
        <DialogHeader>
          <DialogTitle>
            {{
              lifecycleAction === 'restore'
                ? t('admin.competitionDetail.restoreChallengeTitle')
                : t('admin.competitionDetail.deleteChallengeTitle')
            }}
          </DialogTitle>
          <DialogDescription>
            {{
              lifecycleAction === 'restore'
                ? t('admin.competitionDetail.restoreChallengeDescription')
                : t('admin.competitionDetail.deleteChallengeDescription')
            }}
          </DialogDescription>
        </DialogHeader>
        <div v-if="selectedChallenge" class="space-y-2 border bg-muted/30 p-4">
          <p class="font-medium">
            {{ displayTitle(selectedChallenge) }}
          </p>
          <code class="block break-all font-mono text-xs text-muted-foreground">
            {{ selectedChallenge.id }}
          </code>
        </div>
        <DialogFooter>
          <Button
            variant="outline"
            :disabled="lifecycleMutation.isPending.value"
            @click="onLifecycleDialogChange(false)"
          >
            {{ t('common.cancel') }}
          </Button>
          <Button
            :variant="lifecycleAction === 'restore' ? 'default' : 'destructive'"
            :disabled="lifecycleMutation.isPending.value"
            @click="confirmLifecycleAction"
          >
            <Loader2 v-if="lifecycleMutation.isPending.value" class="size-4 animate-spin" />
            {{
              lifecycleAction === 'restore'
                ? t('admin.competitionDetail.restoreChallenge')
                : t('common.delete')
            }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </section>
</template>
