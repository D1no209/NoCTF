<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdDeliveriesPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdDeliveriesPage'
const props = defineProps<{ state: AdminCompetitionsByIdDeliveriesPageViewState }>()
const { RefreshCw, Copy, competitionId, challengeOptions, teamOptions, filterChallenge, filterTeam, includeHistory, items, loading, listError, initialized, page, pageCount, total, pageLimit, loadPage, setPageSize, applyFilters, clear, refresh, attachmentName, stateKey, copy, adminTeamPath, adminChallengePath, adminFormatDateTime } = toRefs(props.state)
</script>
<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-3"><h1 class="text-display text-xl">{{ $t('challengeDelivery.title') }}</h1>
      <Button variant="ghost" :disabled="loading" @click="refresh"><RefreshCw data-icon="inline-start" />{{ $t('challengeDelivery.refresh') }}</Button>
    </div>
    <div class="flex flex-wrap items-center gap-3">
      <Select v-model="filterChallenge"><SelectTrigger class="w-56" :aria-label="$t('common.label.topics')"><SelectValue :placeholder="$t('common.label.topics')" /></SelectTrigger>
        <SelectContent><SelectGroup><SelectItem v-for="item in challengeOptions" :key="item.id" :value="item.id">{{ item.title }}</SelectItem></SelectGroup></SelectContent></Select>
      <Select v-model="filterTeam"><SelectTrigger class="w-48" :aria-label="$t('common.label.team')"><SelectValue :placeholder="$t('common.label.team')" /></SelectTrigger>
        <SelectContent><SelectGroup><SelectItem v-for="item in teamOptions" :key="item.id" :value="item.id">{{ item.name }}</SelectItem></SelectGroup></SelectContent></Select>
      <Field orientation="horizontal"><Switch id="delivery-history" v-model="includeHistory" /><FieldLabel for="delivery-history">{{ $t('challengeDelivery.includeHistory') }}</FieldLabel></Field>
      <Button :disabled="loading" @click="applyFilters">{{ $t('common.label.applyFilters') }}</Button>
      <Button variant="ghost" :disabled="loading" @click="clear">{{ $t('common.label.clear') }}</Button>
    </div>
    <Alert v-if="listError" variant="destructive"><AlertDescription>{{ $message(listError.message) }}</AlertDescription></Alert>
    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && !listError && !items.length"><EmptyHeader><EmptyTitle>{{ $t('challengeDelivery.empty') }}</EmptyTitle></EmptyHeader></Empty>
    <Table v-else-if="items.length"><TableHeader><TableRow>
      <TableHead>{{ $t('common.label.team') }}</TableHead><TableHead>{{ $t('common.label.challenge.pageTitle') }}</TableHead>
      <TableHead>{{ $t('challengeDelivery.attachment') }}</TableHead><TableHead>{{ $t('common.label.flag.flagSubmitView') }}</TableHead>
    </TableRow></TableHeader><TableBody><TableRow v-for="item in items" :key="`${item.teamId}:${item.competitionChallengeId}`">
      <TableCell class="align-top font-medium"><NuxtLink :to="adminTeamPath(competitionId, item.teamId)" class="hover:underline">{{ item.teamName }}</NuxtLink></TableCell>
      <TableCell class="align-top"><NuxtLink :to="adminChallengePath(competitionId, item.competitionChallengeId)" class="hover:underline">{{ item.challengeTitle }}</NuxtLink></TableCell>
      <TableCell class="align-top"><div v-if="item.attachments?.length" class="flex flex-col gap-2"><div v-for="file in item.attachments" :key="file.id" class="max-w-64 break-words whitespace-normal text-sm">
        {{ file.fileName }}<Badge v-if="file.deleted" variant="secondary">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
      </div></div><span v-else class="text-sm text-muted-foreground">{{ $t(item.randomAttachment ? 'challengeDelivery.notAssigned' : 'common.label.symbol') }}</span></TableCell>
      <TableCell class="align-top"><div v-if="item.flags?.length" class="flex flex-col gap-3"><div v-for="flag in item.flags" :key="flag.id" class="flex flex-col gap-1">
        <div class="flex items-start gap-2"><code class="max-w-xl min-w-0 flex-1 break-all whitespace-pre-wrap font-mono text-sm">{{ flag.flag }}</code>
          <Button variant="ghost" size="icon" :aria-label="$t('challengeDelivery.copyFlag')" @click="copy(flag)"><Copy /></Button></div>
        <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"><Badge variant="secondary">{{ $t(stateKey(flag)) }}</Badge>
          <span v-if="flag.attachmentId">{{ attachmentName(item, flag) }}</span>
          <Badge v-if="flag.matchKind === 'RegularExpression'" variant="secondary">{{ $t('runtimeFlags.regex') }}</Badge>
          <span v-if="flag.validStart || flag.validUntil" class="font-mono tabular-nums">{{ adminFormatDateTime(flag.validStart) }} · {{ adminFormatDateTime(flag.validUntil) }}</span>
        </div>
      </div></div><span v-else class="text-sm text-muted-foreground">{{ $t('challengeDelivery.notIssued') }}</span></TableCell>
    </TableRow></TableBody></Table>
    <OffsetPagination v-if="total > 0" :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading"
      @update:page="loadPage" @update:limit="setPageSize" />
  </div>
</template>
