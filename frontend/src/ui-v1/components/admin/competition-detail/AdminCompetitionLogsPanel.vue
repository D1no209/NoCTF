<script setup lang="ts">
import { Loader2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Card } from '@/ui-v1/components/ui/card'

interface CompetitionLogDto {
  id: string
  level: string
  eventType: string
  message: string
  teamName?: string | null
  challengeTitle?: string | null
  createdAt: string
}

defineProps<{
  competitionLogs?: CompetitionLogDto[]
  loadingLogs: boolean
}>()

const { t } = useI18n()
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">{{ t('admin.competitionDetail.logsTitle') }}</h3>
      <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.logsDescription') }}</p>
    </div>
    <div v-if="loadingLogs" class="py-8 text-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>
    <div v-else class="max-h-[360px] space-y-2 overflow-y-auto pr-1">
      <div v-for="log in competitionLogs ?? []" :key="log.id" class="rounded-lg border bg-muted/30 p-3 text-sm">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <Badge :variant="log.level === 'error' ? 'destructive' : log.level === 'warning' ? 'secondary' : 'outline'">
            {{ log.eventType }}
          </Badge>
          <span class="text-xs text-muted-foreground">{{ new Date(log.createdAt).toLocaleString() }}</span>
        </div>
        <p class="mt-2">{{ log.message }}</p>
        <p v-if="log.teamName || log.challengeTitle" class="mt-1 text-xs text-muted-foreground">{{ log.teamName || '-' }} · {{ log.challengeTitle || '-' }}</p>
      </div>
      <div v-if="!competitionLogs?.length" class="flex flex-col items-center justify-center py-8 text-center text-sm text-muted-foreground">
        {{ t('admin.competitionDetail.noLogs') }}
      </div>
    </div>
  </Card>
</template>