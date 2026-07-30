<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Loader2, Save } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import {
  ApiError,
  challengeBankAdminApi,
  competitionAdminApi,
  competitionChallengeAdminApi,
} from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { challengeModeLabelKey } from '@/components/admin/challenges/challengeTemplatePresentation'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  buildCompetitionChallengeCreateRequest,
  buildCompetitionChallengeUpdateRequest,
  isAvailableCompetitionChallengeTemplate,
  isValidOptionalCompetitionChallengeId,
  nextCompetitionChallengeOrder,
} from './competitionChallengeLifecycle'

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const { t } = useI18n()
const competitionId = computed(() => String(route.params.id))
const competitionChallengeId = computed(() =>
  typeof route.params.competitionChallengeId === 'string'
    ? route.params.competitionChallengeId
    : '',
)
const editing = computed(() => Boolean(competitionChallengeId.value))
const orderInitialized = ref(false)

const form = reactive({
  stableId: '',
  challengeId: '',
  baseScore: 500,
  order: 0,
  isPublished: false,
})

const competitionQuery = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionSummary(competitionId.value)),
  queryFn: () => competitionAdminApi.get(competitionId.value),
})

const templatesQuery = useQuery({
  queryKey: computed(() => [...queryKeys.adminChallenges, { includeDeleted: false }]),
  queryFn: () => challengeBankAdminApi.templates(false),
  enabled: computed(() => !editing.value),
})

const createInventoryQuery = useQuery({
  queryKey: computed(() => [
    ...queryKeys.adminCompetitionChallenges(competitionId.value),
    { includeDeleted: true },
  ]),
  queryFn: () => competitionChallengeAdminApi.list(competitionId.value, true),
  enabled: computed(() => !editing.value),
})

const challengeQuery = useQuery({
  queryKey: computed(() =>
    queryKeys.adminCompetitionChallenge(
      competitionId.value,
      competitionChallengeId.value || 'unselected',
      true,
    ),
  ),
  queryFn: () =>
    competitionChallengeAdminApi.get(
      competitionId.value,
      competitionChallengeId.value,
      true,
    ),
  enabled: editing,
})

const compatibleTemplates = computed(() => {
  const mode = competitionQuery.data.value?.mode
  if (mode === undefined)
    return []

  return (templatesQuery.data.value ?? []).filter(
    template => template.id && template.mode === mode,
  )
})

const competitionModeLabel = computed(() => {
  const mode = competitionQuery.data.value?.mode
  return mode === undefined ? '' : t(challengeModeLabelKey(mode))
})
const stableIdIsValid = computed(() =>
  isValidOptionalCompetitionChallengeId(form.stableId),
)
const selectedTemplateIsCompatible = computed(() =>
  isAvailableCompetitionChallengeTemplate(form.challengeId, compatibleTemplates.value),
)
const draft = computed(() => ({
  stableId: form.stableId,
  challengeId: form.challengeId,
  baseScore: Number(form.baseScore),
  order: Number(form.order),
  isPublished: form.isPublished,
}))
const createRequest = computed(() =>
  editing.value || !selectedTemplateIsCompatible.value
    ? null
    : buildCompetitionChallengeCreateRequest(draft.value),
)
const updateRequest = computed(() => {
  const challenge = challengeQuery.data.value
  return editing.value && challenge
    ? buildCompetitionChallengeUpdateRequest(challenge, draft.value)
    : null
})
const requestBody = computed(() => {
  return editing.value ? updateRequest.value : createRequest.value
})
const isLoading = computed(
  () =>
    competitionQuery.isLoading.value
    || (editing.value
      ? challengeQuery.isLoading.value
      : templatesQuery.isLoading.value || createInventoryQuery.isLoading.value),
)
const isError = computed(
  () =>
    competitionQuery.isError.value
    || (editing.value
      ? challengeQuery.isError.value
      : templatesQuery.isError.value || createInventoryQuery.isError.value),
)

watch(
  compatibleTemplates,
  (templates) => {
    if (
      !editing.value
      && !isAvailableCompetitionChallengeTemplate(form.challengeId, templates)
    ) {
      form.challengeId = templates[0]?.id ?? ''
    }
  },
  { immediate: true },
)

watch(
  () => createInventoryQuery.data.value,
  (challenges) => {
    if (editing.value || orderInitialized.value || !challenges)
      return

    form.order = nextCompetitionChallengeOrder(challenges)
    orderInitialized.value = true
  },
  { immediate: true },
)

watch(
  () => challengeQuery.data.value,
  (challenge) => {
    if (!challenge)
      return

    form.stableId = challenge.id ?? ''
    form.challengeId = challenge.challengeId ?? ''
    form.baseScore = challenge.baseScore ?? 0
    form.order = challenge.order ?? 0
    form.isPublished = challenge.isPublished ?? false
  },
  { immediate: true },
)

const saveMutation = useMutation({
  mutationFn: async () => {
    if (editing.value) {
      const body = updateRequest.value
      if (!body)
        throw new Error('Competition challenge update is incomplete.')

      return await competitionChallengeAdminApi.update(
        competitionId.value,
        competitionChallengeId.value,
        body,
      )
    }

    const body = createRequest.value
    if (!body)
      throw new Error('Competition challenge creation is incomplete.')

    return await competitionChallengeAdminApi.create(competitionId.value, body)
  },
  onSuccess: async () => {
    await queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionChallenges(competitionId.value),
    })
    toast.success(
      t(
        editing.value
          ? 'admin.competitionDetail.updateChallengeSuccess'
          : 'admin.competitionDetail.deploySuccess',
      ),
    )
    await returnToChallenges()
  },
  onError: async (error) => {
    const staleEditorState
      = error instanceof ApiError
        && (error.status === 409 || error.status === 404)
    if (staleEditorState) {
      if (editing.value) {
        await challengeQuery.refetch()
      }
      else {
        await Promise.all([
          competitionQuery.refetch(),
          templatesQuery.refetch(),
          createInventoryQuery.refetch(),
        ])
      }

      toast.error(t('admin.competitionDetail.challengeRevisionConflict'))
      return
    }

    toast.error(
      t(
        editing.value
          ? 'admin.competitionDetail.updateChallengeError'
          : 'admin.competitionDetail.deployError',
      ),
    )
  },
})

function returnToChallenges() {
  return router.push({
    name: 'admin-competition-detail',
    params: { id: competitionId.value },
    query: { section: 'challenges' },
  })
}

function refreshEditor() {
  void competitionQuery.refetch()
  if (editing.value) {
    void challengeQuery.refetch()
  }
  else {
    void templatesQuery.refetch()
    void createInventoryQuery.refetch()
  }
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6">
    <header class="flex flex-col gap-4 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <Button variant="ghost" size="sm" class="-ml-2 mb-2" @click="returnToChallenges">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitionDetail.backToChallenges') }}
        </Button>
        <h2 class="text-2xl font-bold tracking-tight">
          {{
            t(
              editing
                ? 'admin.competitionDetail.editCompetitionChallenge'
                : 'admin.competitionDetail.createCompetitionChallenge',
            )
          }}
        </h2>
        <p class="text-sm text-muted-foreground">
          {{ competitionQuery.data.value?.title }}
          <template v-if="competitionModeLabel">
            · {{ competitionModeLabel }}
          </template>
        </p>
      </div>
      <Button
        :disabled="isLoading || saveMutation.isPending.value || !requestBody"
        @click="saveMutation.mutate()"
      >
        <Loader2 v-if="saveMutation.isPending.value" class="size-4 animate-spin" />
        <Save v-else class="size-4" />
        {{ t('common.save') }}
      </Button>
    </header>

    <div
      v-if="isLoading"
      class="flex min-h-80 items-center justify-center text-sm text-muted-foreground"
    >
      <Loader2 class="mr-2 size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>

    <Alert v-else-if="isError" variant="destructive">
      <AlertTitle>{{ t('admin.competitionDetail.challengeEditorLoadError') }}</AlertTitle>
      <AlertDescription class="mt-3">
        <Button variant="outline" size="sm" @click="refreshEditor">
          {{ t('common.retry') }}
        </Button>
      </AlertDescription>
    </Alert>

    <template v-else>
      <Alert v-if="editing && challengeQuery.data.value?.deletedAt" variant="destructive">
        <AlertTitle>{{ t('admin.competitionDetail.challengeDeleted') }}</AlertTitle>
        <AlertDescription>
          {{ t('admin.competitionDetail.deletedChallengeEditBlocked') }}
        </AlertDescription>
      </Alert>

      <Alert v-if="!editing && compatibleTemplates.length === 0">
        <AlertTitle>{{ t('admin.competitionDetail.noMatchingTemplates') }}</AlertTitle>
        <AlertDescription>
          {{ t('admin.competitionDetail.noMatchingTemplatesDescription') }}
        </AlertDescription>
      </Alert>

      <Card class="p-0">
        <CardContent class="grid gap-5 p-5 sm:grid-cols-2">
          <div v-if="!editing" class="grid gap-2 sm:col-span-2">
            <Label for="competition-challenge-stable-id">
              {{ t('admin.competitionDetail.stableChallengeId') }}
            </Label>
            <Input
              id="competition-challenge-stable-id"
              v-model="form.stableId"
              autocomplete="off"
              :aria-invalid="!stableIdIsValid"
              :placeholder="t('admin.competitionDetail.stableChallengeIdPlaceholder')"
            />
            <p
              class="text-xs"
              :class="stableIdIsValid ? 'text-muted-foreground' : 'text-destructive'"
            >
              {{
                stableIdIsValid
                  ? t('admin.competitionDetail.stableChallengeIdHint')
                  : t('admin.competitionDetail.invalidStableChallengeId')
              }}
            </p>
          </div>

          <div v-else class="grid gap-2 sm:col-span-2">
            <div class="flex items-center justify-between gap-3">
              <Label>{{ t('admin.competitionDetail.stableChallengeId') }}</Label>
              <Badge variant="outline">
                v{{ challengeQuery.data.value?.revision ?? '—' }}
              </Badge>
            </div>
            <code class="block break-all border bg-muted/30 p-3 font-mono text-xs">
              {{ challengeQuery.data.value?.id }}
            </code>
          </div>

          <div class="grid gap-2 sm:col-span-2">
            <Label for="competition-challenge-template">
              {{ t('admin.competitionDetail.challengeTemplate') }}
            </Label>
            <Select
              v-if="!editing"
              v-model="form.challengeId"
              :disabled="compatibleTemplates.length === 0"
            >
              <SelectTrigger id="competition-challenge-template">
                <SelectValue :placeholder="t('admin.competitionDetail.selectTemplate')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="template in compatibleTemplates"
                  :key="template.id"
                  :value="template.id ?? ''"
                >
                  {{ template.title || template.id }} · {{ template.direction }}
                </SelectItem>
              </SelectContent>
            </Select>
            <code v-else class="block break-all border bg-muted/30 p-3 font-mono text-xs">
              {{ challengeQuery.data.value?.challengeId }}
            </code>
          </div>

          <div class="grid gap-2">
            <Label for="competition-challenge-base-score">
              {{ t('admin.competitionDetail.baseScore') }}
            </Label>
            <Input
              id="competition-challenge-base-score"
              v-model.number="form.baseScore"
              type="number"
              min="0"
              step="1"
            />
          </div>

          <div class="grid gap-2">
            <Label for="competition-challenge-order">
              {{ t('admin.competitionDetail.challengeOrder') }}
            </Label>
            <Input
              id="competition-challenge-order"
              v-model.number="form.order"
              type="number"
              min="0"
              step="1"
            />
          </div>

          <label
            v-if="editing"
            class="flex min-h-11 cursor-pointer items-center gap-3 border px-3 py-2 text-sm sm:col-span-2"
          >
            <input v-model="form.isPublished" type="checkbox" class="size-4">
            <span>{{ t('admin.competitionDetail.publishChallenge') }}</span>
          </label>
          <p v-else class="text-xs text-muted-foreground sm:col-span-2">
            {{ t('admin.competitionDetail.newChallengeStartsDraft') }}
          </p>
        </CardContent>
      </Card>

      <div class="flex justify-end gap-2 border-t pt-5">
        <Button variant="outline" @click="returnToChallenges">
          {{ t('common.cancel') }}
        </Button>
        <Button
          :disabled="saveMutation.isPending.value || !requestBody"
          @click="saveMutation.mutate()"
        >
          <Loader2 v-if="saveMutation.isPending.value" class="size-4 animate-spin" />
          <Save v-else class="size-4" />
          {{ t('common.save') }}
        </Button>
      </div>
    </template>
  </div>
</template>
