<script setup lang="ts">
import { toRefs } from 'vue'
import type { StaffWorkbenchViewState } from '~/features/competitions/staff-webhooks/useStaffWorkbench'
const props = defineProps<{ state: StaffWorkbenchViewState }>()
const { competition, error, ready, panel, pending, kindKeys, choose, itemPath, load, CheatsPage, TeamsPage, StaffWebhooks } = toRefs(props.state)
</script>
<template>
  <div class="flex flex-col gap-5">
    <div class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-display text-2xl">{{ $t('staffWebhook.workbench') }} · {{ competition?.title }}</h1><Button variant="secondary" @click="load">{{ $t('common.label.reload') }}</Button></div>
    <Alert v-if="error" variant="destructive"><AlertTitle>{{ $t('common.error.requestFailed') }}</AlertTitle><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <template v-else-if="ready">
      <div class="flex flex-wrap gap-2"><Button :variant="panel === 'Pending' ? 'default' : 'secondary'" @click="choose('Pending')">{{ $t('staffWebhook.pending') }}</Button><Button :variant="panel === 'CheatIncident' ? 'default' : 'secondary'" @click="choose('CheatIncident')">{{ $t('staffWebhook.cheatIncident') }}</Button><Button :variant="panel === 'BanAppeal' ? 'default' : 'secondary'" @click="choose('BanAppeal')">{{ $t('staffWebhook.banAppeal') }}</Button><Button :variant="panel === 'Webhooks' ? 'default' : 'secondary'" @click="choose('Webhooks')">{{ $t('staffWebhook.title') }}</Button></div>
      <component :is="CheatsPage" v-if="panel === 'CheatIncident'" />
      <component :is="TeamsPage" v-else-if="panel === 'BanAppeal'" />
      <component :is="StaffWebhooks" v-else-if="panel === 'Webhooks'" />
      <Card v-else><CardHeader><CardTitle>{{ $t('staffWebhook.pending') }}</CardTitle></CardHeader><CardContent class="flex flex-col gap-4">
        <Skeleton v-if="pending.loading.value" class="h-32 w-full" />
        <Alert v-else-if="pending.error.value" variant="destructive"><AlertDescription>{{ $message(pending.error.value.displayMessage) }}</AlertDescription></Alert>
        <Empty v-else-if="!pending.items.value.length"><EmptyHeader><EmptyTitle>{{ $t('staffWebhook.noPending') }}</EmptyTitle></EmptyHeader></Empty>
        <div v-for="item in pending.items.value" :key="item.id" class="flex flex-wrap items-center gap-3 py-2"><Badge>{{ $t(kindKeys[item.kind ?? 0]) }}</Badge><div class="min-w-0 flex-1"><p class="break-words">{{ item.teamName ?? '—' }} · {{ item.challengeTitle ?? $t('staffWebhook.platform') }}</p><time class="text-sm text-muted-foreground">{{ formatDateTime(item.actionRequiredSince) }}</time></div><Button as-child variant="secondary"><NuxtLink :to="itemPath(item)">{{ $t('staffWebhook.open') }}</NuxtLink></Button></div>
        <OffsetPagination :page="pending.page.value" :page-count="pending.pageCount.value" :total="pending.total.value" :limit="pending.limit.value" :loading="pending.loading.value" @update:page="pending.loadPage" @update:limit="pending.setPageSize" />
      </CardContent></Card>
    </template>
    <Skeleton v-else class="h-40 w-full" />
  </div>
</template>
