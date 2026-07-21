<script setup lang="ts">
import { Check, Loader2, Lock, ShieldAlert, Unlock, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'

interface CompetitionTeamDto {
  id: string
  name: string
  captainName: string
  memberCount: number
  inviteToken: string
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  registeredAt: string
  approvedAt?: string | null
}

defineProps<{
  competitionTeams?: CompetitionTeamDto[]
  loadingTeams: boolean
  maxTeamMembers: number | string
}>()

const emit = defineEmits<{
  approve: [teamId: string]
  reject: [teamId: string]
  toggleLock: [payload: { teamId: string, isLocked: boolean }]
  toggleBan: [payload: { teamId: string, isBanned: boolean }]
}>()

const { t } = useI18n()
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
          <TableHead>{{ t('admin.competitionDetail.track') }}</TableHead>
          <TableHead>{{ t('common.status') }}</TableHead>
          <TableHead>{{ t('admin.competitionDetail.locked') }}</TableHead>
          <TableHead>{{ t('admin.competitionDetail.banned') }}</TableHead>
          <TableHead class="text-right">{{ t('common.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="loadingTeams">
          <TableCell colspan="7" class="h-20 text-center text-muted-foreground">
            <Loader2 class="mr-2 inline size-4 animate-spin" />
            {{ t('common.loading') }}
          </TableCell>
        </TableRow>
        <TableRow v-else-if="!competitionTeams?.length">
          <TableCell colspan="7" class="h-20 text-center text-muted-foreground">
            {{ t('admin.competitionDetail.noTeams') }}
          </TableCell>
        </TableRow>
        <TableRow v-for="team in competitionTeams" v-else :key="team.id">
          <TableCell>
            <div class="font-medium">{{ team.name }}</div>
            <div class="text-xs text-muted-foreground">{{ team.captainName }}</div>
            <code class="mt-1 block text-[10px] text-muted-foreground">{{ team.inviteToken }}</code>
          </TableCell>
          <TableCell>{{ team.memberCount }} / {{ maxTeamMembers }}</TableCell>
          <TableCell class="text-xs text-muted-foreground">{{ team.trackName || '-' }}</TableCell>
          <TableCell>
            <Badge :variant="team.registrationStatus === 'approved' ? 'default' : team.registrationStatus === 'rejected' ? 'destructive' : 'secondary'">
              {{ team.registrationStatus }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="team.isLocked ? 'outline' : 'secondary'">
              {{ team.isLocked ? t('admin.competitionDetail.locked') : t('admin.competitionDetail.unlocked') }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="team.isBanned ? 'destructive' : 'secondary'">
              {{ team.isBanned ? t('admin.competitionDetail.banned') : t('admin.competitionDetail.normal') }}
            </Badge>
          </TableCell>
          <TableCell class="text-right">
            <div class="flex justify-end gap-1">
              <Button v-if="team.registrationStatus !== 'approved'" variant="ghost" size="icon" class="size-8" @click="emit('approve', team.id)"><Check class="size-4" /></Button>
              <Button v-if="team.registrationStatus !== 'rejected'" variant="ghost" size="icon" class="size-8 text-destructive" @click="emit('reject', team.id)"><X class="size-4" /></Button>
              <Button variant="ghost" size="icon" class="size-8" @click="emit('toggleLock', { teamId: team.id, isLocked: !team.isLocked })"><Unlock v-if="team.isLocked" class="size-4" /><Lock v-else class="size-4" /></Button>
              <Button variant="ghost" size="icon" class="size-8" :class="team.isBanned ? '' : 'text-destructive'" @click="emit('toggleBan', { teamId: team.id, isBanned: team.isBanned })"><ShieldAlert class="size-4" /></Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </Card>
</template>