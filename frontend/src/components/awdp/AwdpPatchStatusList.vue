<script setup lang="ts">
import type { PropType } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'

interface PatchSubmissionStatus {
  id?: string
  submissionId?: string
  challengeId: string
  challengeName?: string
  challengeTitle?: string
  status: 'Pending' | 'Running' | 'Retrying' | 'Applied' | 'Verified' | 'Rejected' | 'Failed'
  submittedAt?: string
  createdAt?: string
  lastError?: string
  validationLog?: string
}

defineProps({
  patchStatuses: {
    type: Array as PropType<PatchSubmissionStatus[]>,
    default: () => [],
  },
})

const { t } = useI18n()
</script>

<template>
  <Panel v-if="patchStatuses.length > 0">
    <CardHeader class="pb-2">
      <CardTitle class="text-sm font-bold uppercase text-muted-foreground">
        {{ t('awd.patchStatus') }}
      </CardTitle>
    </CardHeader>
    <CardContent>
      <div class="divide-y">
        <div
          v-for="status in patchStatuses"
          :key="status.challengeId"
          class="flex items-center justify-between py-2.5 first:pt-0 last:pb-0"
        >
          <span class="text-sm font-medium">{{ status.challengeName ?? status.challengeTitle ?? status.challengeId }}</span>
          <Badge
            :variant="status.status === 'Verified' ? 'default' : status.status === 'Rejected' || status.status === 'Failed' ? 'destructive' : 'secondary'"
            class="h-5 text-[10px] font-bold uppercase tracking-tighter"
          >
            {{ status.status }}
          </Badge>
        </div>
      </div>
    </CardContent>
  </Panel>
</template>