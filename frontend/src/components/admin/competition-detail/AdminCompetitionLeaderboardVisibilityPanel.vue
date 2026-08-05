<script setup lang="ts">
import type { NoCtfDomainCompetitionsCompetitionLeaderboardVisibility } from '@/api/generated/types.gen'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bot, Clock3, Eye, EyeOff, Save, Snowflake } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { leaderboardVisibility, leaderboardVisibilityLabelKey } from '@/api/leaderboardVisibility'
import { competitionAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'

const props = defineProps<{
  competitionId: string
}>()

const { t, locale } = useI18n()
const queryClient = useQueryClient()
const selectedVisibility = ref<NoCtfDomainCompetitionsCompetitionLeaderboardVisibility>(
  leaderboardVisibility.normal,
)
const activation = ref<'immediate' | 'scheduled'>('immediate')
const startsAt = ref('')
const reason = ref('')

const visibilityOptions = [
  { value: leaderboardVisibility.normal, icon: Eye, descriptionKey: 'leaderboardVisibility.normalDescription' },
  { value: leaderboardVisibility.frozen, icon: Snowflake, descriptionKey: 'leaderboardVisibility.frozenDescription' },
  { value: leaderboardVisibility.blackout, icon: EyeOff, descriptionKey: 'leaderboardVisibility.blackoutDescription' },
] as const

const visibilityQuery = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionLeaderboardVisibility(props.competitionId)),
  queryFn: () => competitionAdminApi.getLeaderboardVisibility(props.competitionId),
  enabled: computed(() => Boolean(props.competitionId)),
})

watch(visibilityQuery.data, (value) => {
  if (!value)
    return
  selectedVisibility.value = value.configuredVisibility ?? leaderboardVisibility.normal
  activation.value = value.startsAt && !value.appliedAt ? 'scheduled' : 'immediate'
  startsAt.value = value.startsAt ? toDateTimeLocal(value.startsAt) : ''
  reason.value = ''
}, { immediate: true })

watch(selectedVisibility, (value) => {
  if (value === leaderboardVisibility.normal) {
    activation.value = 'immediate'
    startsAt.value = ''
  }
})

const scheduledAt = computed(() => {
  if (!visibilityQuery.data.value?.startsAt || visibilityQuery.data.value.appliedAt)
    return null
  const date = new Date(visibilityQuery.data.value.startsAt)
  if (Number.isNaN(date.getTime()))
    return visibilityQuery.data.value.startsAt
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(date)
})

function toDateTimeLocal(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return ''
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

const updateMutation = useMutation({
  mutationFn: async () => {
    const scheduled = selectedVisibility.value !== leaderboardVisibility.normal
      && activation.value === 'scheduled'
    const scheduledDate = scheduled ? new Date(startsAt.value) : null
    if (scheduled && (!scheduledDate || !Number.isFinite(scheduledDate.getTime())))
      throw new TypeError('Invalid visibility activation time.')
    return competitionAdminApi.updateLeaderboardVisibility(props.competitionId, {
      visibility: selectedVisibility.value,
      startsAt: scheduledDate?.toISOString() ?? null,
      expectedRevision: visibilityQuery.data.value?.revision ?? 0,
      reason: reason.value.trim() || null,
    })
  },
  onSuccess: async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: queryKeys.adminCompetitionLeaderboardVisibility(props.competitionId),
      }),
      queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetition(props.competitionId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.competition(props.competitionId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.challenges(props.competitionId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(props.competitionId) }),
    ])
    toast.success(t('admin.competitionDetail.leaderboardVisibilitySaved'))
  },
  onError: async () => {
    await visibilityQuery.refetch()
    toast.error(t('admin.competitionDetail.leaderboardVisibilitySaveError'))
  },
})
</script>

<template>
  <Card data-testid="leaderboard-visibility-panel" class="overflow-hidden p-0">
    <div class="flex flex-col gap-3 border-b-2 border-border p-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="max-w-3xl">
        <h3 class="font-semibold">
          {{ t('admin.competitionDetail.leaderboardVisibilityTitle') }}
        </h3>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.competitionDetail.leaderboardVisibilityDescription') }}
        </p>
      </div>
      <div v-if="visibilityQuery.data.value" class="flex shrink-0 flex-wrap gap-2">
        <Badge variant="outline">
          {{ t('admin.competitionDetail.configuredVisibility') }}:
          {{ t(leaderboardVisibilityLabelKey(visibilityQuery.data.value.configuredVisibility)) }}
        </Badge>
        <Badge>
          {{ t('admin.competitionDetail.effectiveVisibility') }}:
          {{ t(leaderboardVisibilityLabelKey(visibilityQuery.data.value.effectiveVisibility)) }}
        </Badge>
      </div>
    </div>

    <div v-if="visibilityQuery.isLoading.value" class="p-4 text-sm text-muted-foreground">
      {{ t('common.loading') }}
    </div>
    <div v-else-if="visibilityQuery.isError.value" class="flex flex-wrap items-center justify-between gap-3 p-4">
      <p class="text-sm text-destructive">
        {{ t('admin.competitionDetail.leaderboardVisibilityLoadError') }}
      </p>
      <Button variant="outline" size="sm" @click="visibilityQuery.refetch()">
        {{ t('common.retry') }}
      </Button>
    </div>
    <div v-else class="space-y-5 p-4">
      <div v-if="scheduledAt" class="flex items-start gap-3 border-2 border-dashed border-border bg-muted/40 p-3 text-sm">
        <Clock3 class="mt-0.5 size-4 shrink-0" />
        <div>
          <p class="font-semibold">
            {{ t('admin.competitionDetail.visibilityScheduled') }}
          </p>
          <p class="text-muted-foreground">
            {{ scheduledAt }}
          </p>
        </div>
      </div>

      <fieldset class="space-y-2">
        <legend class="text-sm font-bold">
          {{ t('admin.competitionDetail.visibilityMode') }}
        </legend>
        <div class="grid gap-2 lg:grid-cols-3">
          <button
            v-for="option in visibilityOptions"
            :key="option.value"
            type="button"
            class="flex min-h-24 items-start gap-3 border-2 p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            :class="selectedVisibility === option.value ? 'border-foreground bg-foreground text-background' : 'border-border bg-card hover:bg-accent'"
            :aria-pressed="selectedVisibility === option.value"
            @click="selectedVisibility = option.value"
          >
            <component :is="option.icon" class="mt-0.5 size-4 shrink-0" />
            <span>
              <span class="block font-bold">{{ t(leaderboardVisibilityLabelKey(option.value)) }}</span>
              <span class="mt-1 block text-xs" :class="selectedVisibility === option.value ? 'text-background/75' : 'text-muted-foreground'">
                {{ t(option.descriptionKey) }}
              </span>
            </span>
          </button>
        </div>
      </fieldset>

      <fieldset v-if="selectedVisibility !== leaderboardVisibility.normal" class="space-y-2">
        <legend class="text-sm font-bold">
          {{ t('admin.competitionDetail.activationMode') }}
        </legend>
        <div class="flex flex-wrap gap-2">
          <Button
            type="button"
            :variant="activation === 'immediate' ? 'default' : 'outline'"
            :aria-pressed="activation === 'immediate'"
            @click="activation = 'immediate'"
          >
            {{ t('admin.competitionDetail.activateImmediately') }}
          </Button>
          <Button
            type="button"
            :variant="activation === 'scheduled' ? 'default' : 'outline'"
            :aria-pressed="activation === 'scheduled'"
            @click="activation = 'scheduled'"
          >
            {{ t('admin.competitionDetail.activateOnSchedule') }}
          </Button>
        </div>
      </fieldset>

      <div v-if="selectedVisibility !== leaderboardVisibility.normal && activation === 'scheduled'" class="grid gap-2">
        <Label for="leaderboard-visibility-starts-at">{{ t('admin.competitionDetail.activationTime') }}</Label>
        <Input id="leaderboard-visibility-starts-at" v-model="startsAt" type="datetime-local" />
        <p class="text-xs text-muted-foreground">
          {{ t('admin.competitionDetail.activationTimeHint') }}
        </p>
      </div>

      <div class="grid gap-2">
        <Label for="leaderboard-visibility-reason">{{ t('admin.competitionDetail.visibilityReason') }}</Label>
        <Textarea id="leaderboard-visibility-reason" v-model="reason" rows="3" maxlength="500" />
      </div>

      <div class="flex flex-col gap-3 border-t-2 border-border pt-4 sm:flex-row sm:items-center sm:justify-between">
        <div class="flex max-w-3xl items-start gap-2 text-xs text-muted-foreground">
          <Bot class="mt-0.5 size-4 shrink-0" />
          <span>{{ t('admin.competitionDetail.blackoutBotHint') }}</span>
        </div>
        <Button :disabled="updateMutation.isPending.value" @click="updateMutation.mutate()">
          <Save class="size-4" />
          {{ t('common.save') }}
        </Button>
      </div>
    </div>
  </Card>
</template>
