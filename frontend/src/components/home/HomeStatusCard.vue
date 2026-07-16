<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'

interface Props {
  joinedTeams: number
  readyTeams: number
  pendingReview: number
}

defineProps<Props>()

const { t } = useI18n()
</script>

<template>
  <Card class="flex h-full flex-col p-0">
    <CardHeader class="px-4 py-3">
      <CardTitle class="text-xs font-bold uppercase tracking-[0.14em]">{{ t('home.statusTitle') }}</CardTitle>
    </CardHeader>

    <CardContent class="grid flex-1 grid-cols-2 gap-3 px-4 pb-4 max-[420px]:grid-cols-1">
      <Panel class="min-h-20 justify-between p-3">
        <span class="text-3xl font-black tracking-normal">{{ joinedTeams }}</span>
        <span class="text-[10px] font-bold uppercase tracking-[0.16em] opacity-80">{{ t('home.joinedTeams') }}</span>
      </Panel>

      <Panel class="min-h-20 justify-between p-3">
        <span class="text-3xl font-black tracking-normal">{{ readyTeams }}</span>
        <span class="text-[10px] font-bold uppercase tracking-[0.16em] opacity-80">{{ t('home.readyTeams') }}</span>
      </Panel>

      <Panel class="col-span-2 min-h-12 flex-row items-center justify-between p-3 max-[420px]:col-span-1">
        <span class="text-sm text-zinc-600">{{ t('home.pendingReview') }}</span>
        <Badge :variant="pendingReview ? 'secondary' : 'outline'">
          {{ pendingReview }}
        </Badge>
      </Panel>
    </CardContent>
  </Card>
</template>
