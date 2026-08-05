<script setup lang="ts">
import type { AdminTeamBanCase } from '@/api/teamBanAppealApi'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Gavel, Inbox, Loader2, ShieldCheck, ShieldX } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { queryKeys } from '@/api/queryKeys'
import {
  teamBanAppealApi,
  TeamBanAppealStatus,
  TeamBanSource,
} from '@/api/teamBanAppealApi'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Textarea } from '@/components/ui/textarea'

const props = defineProps<{ competitionId: string }>()
const { locale, t } = useI18n()
const queryClient = useQueryClient()
const resolutionTarget = ref<AdminTeamBanCase | null>(null)
const resolutionAction = ref<'accept' | 'uphold'>('accept')
const resolutionReason = ref('')

const { data: appeals, isLoading } = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionTeamBanAppeals(props.competitionId)),
  queryFn: () => teamBanAppealApi.listAdmin(props.competitionId),
})

const resolutionMutation = useMutation({
  mutationFn: async () => {
    const appealId = resolutionTarget.value?.appeal?.id
    if (!appealId)
      throw new TypeError('The selected ban appeal has no identifier.')
    const reason = resolutionReason.value.trim()
    if (resolutionAction.value === 'accept')
      await teamBanAppealApi.accept(props.competitionId, appealId, reason)
    else
      await teamBanAppealApi.uphold(props.competitionId, appealId, reason)
  },
  onSuccess: () => {
    void queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionTeamBanAppeals(props.competitionId),
    })
    void queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionTeams(props.competitionId),
    })
    void queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(props.competitionId) })
    toast.success(t('admin.competitionDetail.appealResolved'))
    resolutionTarget.value = null
    resolutionReason.value = ''
  },
  onError: () => toast.error(t('admin.competitionDetail.appealResolutionFailed')),
})

function openResolution(banCase: AdminTeamBanCase, action: 'accept' | 'uphold') {
  resolutionTarget.value = banCase
  resolutionAction.value = action
  resolutionReason.value = ''
}

function formatDate(value?: string | null) {
  if (!value)
    return '-'
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function statusKey(status?: number) {
  if (status === TeamBanAppealStatus.Accepted)
    return 'accepted'
  if (status === TeamBanAppealStatus.Upheld)
    return 'upheld'
  return 'submitted'
}

function statusVariant(status?: number): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === TeamBanAppealStatus.Accepted)
    return 'default'
  if (status === TeamBanAppealStatus.Upheld)
    return 'destructive'
  return 'secondary'
}
</script>

<template>
  <Card class="p-4">
    <div class="mb-5 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h3 class="flex items-center gap-2 font-semibold">
          <Gavel class="size-4" />
          {{ t('admin.competitionDetail.banAppealsTitle') }}
        </h3>
        <p class="mt-1 text-sm text-muted-foreground">
          {{ t('admin.competitionDetail.banAppealsDescription') }}
        </p>
      </div>
      <Badge variant="outline">
        {{ appeals?.length ?? 0 }}
      </Badge>
    </div>

    <div v-if="isLoading" class="flex min-h-28 items-center justify-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 size-4 animate-spin" />{{ t('common.loading') }}
    </div>
    <Panel v-else-if="!appeals?.length" class="flex min-h-28 flex-col items-center justify-center border-dashed text-center text-sm text-muted-foreground">
      <Inbox class="mb-2 size-5" />
      {{ t('admin.competitionDetail.noBanAppeals') }}
    </Panel>
    <div v-else class="space-y-3">
      <Panel v-for="banCase in appeals" :key="banCase.appeal?.id" class="p-4">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div class="flex flex-wrap items-center gap-2">
              <p class="font-semibold">
                {{ banCase.teamName ?? '-' }}
              </p>
              <Badge :variant="statusVariant(banCase.appeal?.status)">
                {{ t(`admin.competitionDetail.appealStatus.${statusKey(banCase.appeal?.status)}`) }}
              </Badge>
              <Badge variant="outline">
                {{ banCase.source === TeamBanSource.CheatIncident
                  ? t('admin.competitionDetail.banSource.cheatIncident')
                  : t('admin.competitionDetail.banSource.manualModeration') }}
              </Badge>
            </div>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ t('admin.competitionDetail.appealSubmittedBy', {
                user: banCase.appeal?.submittedByUserName ?? '-',
                time: formatDate(banCase.appeal?.submittedAt),
              }) }}
            </p>
          </div>
          <div v-if="banCase.canResolve" class="flex gap-2">
            <Button size="sm" variant="outline" @click="openResolution(banCase, 'uphold')">
              <ShieldX class="size-4" />{{ t('admin.competitionDetail.upholdAppeal') }}
            </Button>
            <Button size="sm" @click="openResolution(banCase, 'accept')">
              <ShieldCheck class="size-4" />{{ t('admin.competitionDetail.acceptAppeal') }}
            </Button>
          </div>
        </div>
        <div class="mt-4 border-l-2 border-primary/30 pl-4">
          <p class="text-xs font-bold uppercase tracking-[0.12em] text-muted-foreground">
            {{ t('admin.competitionDetail.appealStatement') }}
          </p>
          <p class="mt-1 whitespace-pre-wrap text-sm">
            {{ banCase.appeal?.statement }}
          </p>
        </div>
        <div v-if="banCase.appeal?.resolutionReason" class="mt-4 border-l-2 border-border pl-4">
          <p class="text-xs font-bold uppercase tracking-[0.12em] text-muted-foreground">
            {{ t('admin.competitionDetail.resolutionReason') }}
          </p>
          <p class="mt-1 whitespace-pre-wrap text-sm">
            {{ banCase.appeal.resolutionReason }}
          </p>
          <p class="mt-1 text-xs text-muted-foreground">
            {{ banCase.appeal.resolvedByUserName ?? '-' }} · {{ formatDate(banCase.appeal.resolvedAt) }}
          </p>
        </div>
      </Panel>
    </div>
  </Card>

  <Dialog :open="Boolean(resolutionTarget)" @update:open="value => !value && (resolutionTarget = null)">
    <DialogContent class="sm:max-w-[500px]">
      <DialogHeader>
        <DialogTitle>
          {{ resolutionAction === 'accept'
            ? t('admin.competitionDetail.acceptAppealTitle')
            : t('admin.competitionDetail.upholdAppealTitle') }}
        </DialogTitle>
        <DialogDescription>
          {{ resolutionAction === 'accept'
            ? t('admin.competitionDetail.acceptAppealDescription')
            : t('admin.competitionDetail.upholdAppealDescription') }}
        </DialogDescription>
      </DialogHeader>
      <div class="space-y-2">
        <Label for="appeal-resolution-reason">{{ t('admin.competitionDetail.resolutionReason') }}</Label>
        <Textarea id="appeal-resolution-reason" v-model="resolutionReason" rows="4" maxlength="512" />
      </div>
      <DialogFooter>
        <Button variant="outline" @click="resolutionTarget = null">
          {{ t('common.cancel') }}
        </Button>
        <Button
          :variant="resolutionAction === 'uphold' ? 'destructive' : 'default'"
          :disabled="resolutionReason.trim().length < 8 || resolutionMutation.isPending.value"
          @click="resolutionMutation.mutate()"
        >
          <Loader2 v-if="resolutionMutation.isPending.value" class="size-4 animate-spin" />
          {{ resolutionAction === 'accept'
            ? t('admin.competitionDetail.acceptAppeal')
            : t('admin.competitionDetail.upholdAppeal') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
