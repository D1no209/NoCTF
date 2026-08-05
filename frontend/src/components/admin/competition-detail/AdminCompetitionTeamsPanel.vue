<script setup lang="ts">
import type { NoCtfapiEndpointsTeamsTeamResponse } from '@/api/generated/types.gen'
import { Check, Gavel, Loader2, ShieldAlert, ShieldCheck, X } from 'lucide-vue-next'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'

defineProps<{
  competitionTeams?: NoCtfapiEndpointsTeamsTeamResponse[]
  loadingTeams: boolean
  maxTeamMembers: number
}>()

const emit = defineEmits<{
  approve: [teamId: string]
  reject: [teamId: string]
  ban: [payload: { teamId: string, reason: string }]
  unban: [teamId: string]
  correct: [payload: { teamId: string, reason: string }]
}>()

const { t } = useI18n()
const registrationStatuses = ['pending', 'approved', 'rejected'] as const
const banTarget = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const correctionTarget = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const moderationReason = ref('')

function statusLabel(status: NoCtfapiEndpointsTeamsTeamResponse['registrationStatus']) {
  return status === undefined ? 'unknown' : registrationStatuses[status]
}

function openBan(team: NoCtfapiEndpointsTeamsTeamResponse) {
  moderationReason.value = ''
  banTarget.value = team
}

function openCorrection(team: NoCtfapiEndpointsTeamsTeamResponse) {
  moderationReason.value = ''
  correctionTarget.value = team
}

function submitBan() {
  if (!banTarget.value?.id || !moderationReason.value.trim())
    return
  emit('ban', { teamId: banTarget.value.id, reason: moderationReason.value.trim() })
  banTarget.value = null
  moderationReason.value = ''
}

function submitCorrection() {
  if (!correctionTarget.value?.id || moderationReason.value.trim().length < 8)
    return
  emit('correct', {
    teamId: correctionTarget.value.id,
    reason: moderationReason.value.trim(),
  })
  correctionTarget.value = null
  moderationReason.value = ''
}
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">
        {{ t('admin.competitionDetail.teamReviewTitle') }}
      </h3>
      <p class="text-sm text-muted-foreground">
        {{ t('admin.competitionDetail.teamReviewDescription') }}
      </p>
    </div>
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{{ t('admin.teams.name') }}</TableHead>
          <TableHead>{{ t('admin.teams.members') }}</TableHead>
          <TableHead>{{ t('admin.teams.captain') }}</TableHead>
          <TableHead>{{ t('common.status') }}</TableHead>
          <TableHead class="text-right">
            {{ t('common.actions') }}
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="loadingTeams">
          <TableCell colspan="5" class="h-20 text-center text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />{{ t('common.loading') }}
          </TableCell>
        </TableRow>
        <TableRow v-else-if="!competitionTeams?.length">
          <TableCell colspan="5" class="h-20 text-center text-muted-foreground">
            {{ t('admin.competitionDetail.noTeams') }}
          </TableCell>
        </TableRow>
        <TableRow v-for="team in competitionTeams" v-else :key="team.id">
          <TableCell>
            <div class="font-medium">
              {{ team.name ?? '-' }}
            </div>
            <code class="text-[10px] text-muted-foreground">{{ team.id }}</code>
          </TableCell>
          <TableCell>{{ team.memberIds?.length ?? 0 }} / {{ maxTeamMembers }}</TableCell>
          <TableCell><code class="text-xs text-muted-foreground">{{ team.captainId ?? '-' }}</code></TableCell>
          <TableCell>
            <div class="flex flex-wrap gap-2">
              <Badge :variant="statusLabel(team.registrationStatus) === 'approved' ? 'default' : statusLabel(team.registrationStatus) === 'rejected' ? 'destructive' : 'secondary'">
                {{ statusLabel(team.registrationStatus) }}
              </Badge>
              <Badge v-if="team.isBanned" variant="destructive">
                {{ t('admin.competitionDetail.teamBanned') }}
              </Badge>
            </div>
          </TableCell>
          <TableCell class="text-right">
            <div v-if="team.id" class="flex flex-wrap justify-end gap-1">
              <Button variant="ghost" size="icon" class="size-8" :title="t('admin.competitionDetail.approveTeam')" @click="emit('approve', team.id)">
                <Check class="size-4" />
              </Button>
              <Button variant="ghost" size="icon" class="size-8 text-destructive" :title="t('admin.competitionDetail.rejectTeam')" @click="emit('reject', team.id)">
                <X class="size-4" />
              </Button>
              <Button v-if="!team.isBanned" variant="ghost" size="icon" class="size-8 text-destructive" :title="t('admin.competitionDetail.banTeam')" @click="openBan(team)">
                <ShieldAlert class="size-4" />
              </Button>
              <template v-else>
                <Button variant="ghost" size="icon" class="size-8" :title="t('admin.competitionDetail.unbanTeam')" @click="emit('unban', team.id)">
                  <ShieldCheck class="size-4" />
                </Button>
                <Button variant="ghost" size="icon" class="size-8 text-amber-700" :title="t('admin.competitionDetail.correctTeamBan')" @click="openCorrection(team)">
                  <Gavel class="size-4" />
                </Button>
              </template>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </Card>

  <Dialog :open="Boolean(banTarget)" @update:open="value => !value && (banTarget = null)">
    <DialogContent class="sm:max-w-[480px]">
      <DialogHeader>
        <DialogTitle>{{ t('admin.competitionDetail.banDialogTitle', { team: banTarget?.name ?? '-' }) }}</DialogTitle>
        <DialogDescription>{{ t('admin.competitionDetail.banDialogDescription') }}</DialogDescription>
      </DialogHeader>
      <div class="space-y-2">
        <Label for="team-ban-reason">{{ t('admin.competitionDetail.moderationReason') }}</Label>
        <Textarea id="team-ban-reason" v-model="moderationReason" rows="4" maxlength="512" />
      </div>
      <DialogFooter>
        <Button variant="outline" @click="banTarget = null">
          {{ t('common.cancel') }}
        </Button>
        <Button variant="destructive" :disabled="!moderationReason.trim()" @click="submitBan">
          {{ t('admin.competitionDetail.banTeam') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <Dialog :open="Boolean(correctionTarget)" @update:open="value => !value && (correctionTarget = null)">
    <DialogContent class="sm:max-w-[480px]">
      <DialogHeader>
        <DialogTitle>{{ t('admin.competitionDetail.correctionDialogTitle', { team: correctionTarget?.name ?? '-' }) }}</DialogTitle>
        <DialogDescription>{{ t('admin.competitionDetail.correctionDialogDescription') }}</DialogDescription>
      </DialogHeader>
      <div class="space-y-2">
        <Label for="team-correction-reason">{{ t('admin.competitionDetail.correctionReason') }}</Label>
        <Textarea id="team-correction-reason" v-model="moderationReason" rows="4" maxlength="512" />
        <p class="text-xs text-muted-foreground">
          {{ t('admin.competitionDetail.correctionPrivacy') }}
        </p>
      </div>
      <DialogFooter>
        <Button variant="outline" @click="correctionTarget = null">
          {{ t('common.cancel') }}
        </Button>
        <Button :disabled="moderationReason.trim().length < 8" @click="submitCorrection">
          {{ t('admin.competitionDetail.correctTeamBan') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
