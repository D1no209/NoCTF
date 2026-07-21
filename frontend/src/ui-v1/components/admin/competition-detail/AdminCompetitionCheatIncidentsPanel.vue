<script setup lang="ts">
import { Loader2, ShieldAlert } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'

interface CheatIncidentDto {
  id: string
  suspectTeamId: string
  suspectTeamName: string
  victimTeamName?: string | null
  challengeTitle: string
  userName: string
  reason: string
  resolved: boolean
  createdAt: string
}

defineProps<{
  cheatIncidents?: CheatIncidentDto[]
  loadingCheatIncidents: boolean
}>()

const emit = defineEmits<{
  ban: [teamId: string]
}>()

const { t } = useI18n()
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">{{ t('admin.competitionDetail.cheatTitle') }}</h3>
      <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.cheatDescription') }}</p>
    </div>
    <div v-if="loadingCheatIncidents" class="py-8 text-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>
    <div v-else-if="!cheatIncidents?.length" class="flex flex-col items-center justify-center py-8 text-center text-sm text-muted-foreground">
      {{ t('admin.competitionDetail.noCheatIncidents') }}
    </div>
    <div v-else class="space-y-3">
      <div v-for="incident in cheatIncidents" :key="incident.id" class="rounded-lg border bg-muted/30 p-3">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div class="font-medium">{{ incident.suspectTeamName }} → {{ incident.victimTeamName || '-' }}</div>
            <div class="text-xs text-muted-foreground">{{ incident.challengeTitle }} · {{ incident.userName }} · {{ new Date(incident.createdAt).toLocaleString() }}</div>
          </div>
          <Button size="sm" variant="destructive" @click="emit('ban', incident.suspectTeamId)">
            <ShieldAlert class="mr-2 size-4" />
            {{ t('admin.competitionDetail.banTeam') }}
          </Button>
        </div>
      </div>
    </div>
  </Card>
</template>