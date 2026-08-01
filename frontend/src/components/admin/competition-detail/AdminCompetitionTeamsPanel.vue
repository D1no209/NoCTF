<script setup lang="ts">
import type { NoCtfapiEndpointsTeamsTeamResponse } from '@/api/generated/types.gen'
import { Check, Loader2, ShieldAlert, ShieldCheck, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

defineProps<{
  competitionTeams?: NoCtfapiEndpointsTeamsTeamResponse[]
  loadingTeams: boolean
  maxTeamMembers: number
}>()

const emit = defineEmits<{
  approve: [teamId: string]
  reject: [teamId: string]
  ban: [teamId: string]
  unban: [teamId: string]
}>()

const { t } = useI18n()
const registrationStatuses = ['pending', 'approved', 'rejected'] as const

function statusLabel(status: NoCtfapiEndpointsTeamsTeamResponse['registrationStatus']) {
  return status === undefined ? 'unknown' : registrationStatuses[status]
}
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">{{ t('admin.competitionDetail.teamReviewTitle') }}</h3>
      <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.teamReviewDescription') }}</p>
    </div>
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{{ t('admin.teams.name') }}</TableHead>
          <TableHead>{{ t('admin.teams.members') }}</TableHead>
          <TableHead>{{ t('admin.teams.captain') }}</TableHead>
          <TableHead>{{ t('common.status') }}</TableHead>
          <TableHead class="text-right">{{ t('common.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="loadingTeams">
          <TableCell colspan="5" class="h-20 text-center text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />{{ t('common.loading') }}
          </TableCell>
        </TableRow>
        <TableRow v-else-if="!competitionTeams?.length">
          <TableCell colspan="5" class="h-20 text-center text-muted-foreground">{{ t('admin.competitionDetail.noTeams') }}</TableCell>
        </TableRow>
        <TableRow v-for="team in competitionTeams" v-else :key="team.id">
          <TableCell>
            <div class="font-medium">{{ team.name ?? '-' }}</div>
            <code class="text-[10px] text-muted-foreground">{{ team.id }}</code>
          </TableCell>
          <TableCell>{{ team.memberIds?.length ?? 0 }} / {{ maxTeamMembers }}</TableCell>
          <TableCell><code class="text-xs text-muted-foreground">{{ team.captainId ?? '-' }}</code></TableCell>
          <TableCell>
            <Badge :variant="statusLabel(team.registrationStatus) === 'approved' ? 'default' : statusLabel(team.registrationStatus) === 'rejected' ? 'destructive' : 'secondary'">
              {{ statusLabel(team.registrationStatus) }}
            </Badge>
          </TableCell>
          <TableCell class="text-right">
            <div v-if="team.id" class="flex flex-wrap justify-end gap-1">
              <Button variant="ghost" size="icon" class="size-8" :title="t('admin.competitionDetail.approveTeam')" @click="emit('approve', team.id)"><Check class="size-4" /></Button>
              <Button variant="ghost" size="icon" class="size-8 text-destructive" :title="t('admin.competitionDetail.rejectTeam')" @click="emit('reject', team.id)"><X class="size-4" /></Button>
              <Button variant="ghost" size="icon" class="size-8 text-destructive" :title="t('admin.competitionDetail.banTeam')" @click="emit('ban', team.id)"><ShieldAlert class="size-4" /></Button>
              <Button variant="ghost" size="icon" class="size-8" :title="t('admin.competitionDetail.unbanTeam')" @click="emit('unban', team.id)"><ShieldCheck class="size-4" /></Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </Card>
</template>
