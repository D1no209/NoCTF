<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdCheatsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdCheatsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdCheatsPageViewState }>()
const { filterStatus, filterFrom, filterTo, filterError, pendingCount, items, loading, listError, hasMore, loadMore, initialized, loadNextPage, applyFilters, detail, detailOpen, detailLoading, showFlag, openDetail, ActionMeta, resolutionAction, canSubmitResolution, resolutionError, resolutionOpen, resolutionPending, resolutionReason, remainingCharacters, resolutionTargetLabel, openAction, handleResolutionSubmit, handleResolutionOpen, onClickShowFlag } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <Select v-model="filterStatus">
        <SelectTrigger class="w-40">
          <SelectValue :placeholder="$t('ui.statusAll')" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="All">{{ $t('ui.all') }}</SelectItem>
            <SelectItem value="Pending">{{ $t('ui.pending') }}</SelectItem>
            <SelectItem value="Confirmed">{{ $t('ui.confirmed') }}</SelectItem>
            <SelectItem value="Dismissed">{{ $t('ui.dismissed') }}</SelectItem>
            <SelectItem value="Superseded">{{ $t('ui.superseded') }}</SelectItem>
            <SelectItem value="Corrected">{{ $t('ui.corrected') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Input v-model="filterFrom" type="datetime-local" class="w-52" :aria-label="$t('ui.startTime2')" />
      <span class="text-sm text-muted-foreground">{{ $t('ui.to') }}</span>
      <Input v-model="filterTo" type="datetime-local" class="w-52" :aria-label="$t('ui.endTime')" />
      <Button size="sm" @click="applyFilters">{{ $t('ui.applyFilters') }}</Button>
      <Badge v-if="pendingCount !== null && pendingCount > 0" variant="destructive">
        {{ $t('ui.pending2', { count: pendingCount }) }}
      </Badge>
    </div>

    <p class="text-xs text-muted-foreground"> {{ $t('ui.whenAllTimesAreLeftBlankTheLast31Days') }} </p>

    <Alert v-if="filterError || listError" variant="destructive">
      <AlertDescription>{{ filterError ?? listError?.message }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noCheatingIncidentsYet') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('ui.sourceTeam') }}</TableHead>
            <TableHead>{{ $t('ui.flagOwnerTeam') }}</TableHead>
            <TableHead>{{ $t('ui.challenge') }}</TableHead>
            <TableHead class="w-20">{{ $t('ui.type') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.status') }}</TableHead>
            <TableHead class="w-44">{{ $t('ui.detectionTime') }}</TableHead>
            <TableHead class="w-40 text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="i in items" :key="i.gameplayFactId">
            <TableCell class="font-medium">
              {{ i.sourceTeamName }}
              <Badge v-if="i.sourceTeamIsBanned" variant="destructive" class="ml-1">{{ $t('ui.banned2') }}</Badge>
            </TableCell>
            <TableCell>{{ i.ownerTeamName ?? $t('ui.multipleTeamsOrUndetermined') }}</TableCell>
            <TableCell>{{ i.challengeTitle }}</TableCell>
            <TableCell>{{ enumLabel(GameplayFactKindLabel, i.gameplayFactKind) }}</TableCell>
            <TableCell>
              <Badge :variant="i.status === 'Pending' ? 'secondary' : i.status === 'Confirmed' ? 'destructive' : 'outline'">
                {{ enumLabel(CheatIncidentStatusLabel, i.status) }}
              </Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(i.detectedAt) }}</TableCell>
            <TableCell class="text-right">
              <Button variant="ghost" size="sm" @click="openDetail(i.gameplayFactId)">{{ $t('ui.details') }}</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadNextPage">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.loadMore') }} </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('ui.cheatIncidentDetails') }}</SheetTitle>
          <SheetDescription>{{ $t('ui.eventId', { id: detail?.gameplayFactId ?? '-' }) }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.sourceTeam') }}</span><span>{{ detail.sourceTeamName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.flagOwner') }}</span><span>{{ detail.ownerTeamName ?? $t('ui.multipleTeamsOrUndetermined') }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.challenge') }}</span><span>{{ detail.challengeTitle }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.author') }}</span><span>{{ detail.submittedByUserName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.type') }}</span><span>{{ enumLabel(GameplayFactKindLabel, detail.gameplayFactKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.result') }}</span><span>{{ enumLabel(GameplayFactResultLabel, detail.result) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.status') }}</span><span>{{ enumLabel(CheatIncidentStatusLabel, detail.status) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.submissionTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.submittedAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.detectionTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.detectedAt) }}</span></div>
          <template v-if="detail.resolvedByUserName">
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.processor') }}</span><span>{{ detail.resolvedByUserName }}</span></div>
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.processingTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.resolvedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">{{ $t('ui.reasonsForProcessing') }}</span><span class="whitespace-pre-wrap">{{ detail.resolutionReason }}</span></div>
          </template>
          <template v-if="detail.sourceTeamIsBanned">
            <Separator />
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.banTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.sourceTeamBannedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">{{ $t('ui.banReason') }}</span><span class="whitespace-pre-wrap">{{ detail.sourceTeamBanReason ?? '-' }}</span></div>
          </template>
          <Separator />
          <div class="flex flex-col gap-2">
            <span class="text-muted-foreground">{{ $t('ui.submittedFlagEvidence') }}</span>
            <Button variant="outline" size="sm" class="w-fit" @click="onClickShowFlag">
              {{ showFlag ? $t('ui.hide') : $t('ui.showFullFlag') }}
            </Button>
            <div v-if="showFlag" class="rounded-md border bg-muted p-3 font-mono text-xs break-all">
              {{ detail.value }}
            </div>
          </div>
          <div v-if="detail.canConfirm || detail.canDismiss || detail.canCorrect" class="flex flex-wrap gap-2 pt-2">
            <Button v-if="detail.canConfirm" variant="destructive" size="sm" @click="openAction('confirm')"> {{ $t('ui.confirmCheatingBan') }} </Button>
            <Button v-if="detail.canDismiss" variant="outline" size="sm" @click="openAction('dismiss')"> {{ $t('ui.dismiss') }} </Button>
            <Button v-if="detail.canCorrect" variant="outline" size="sm" @click="openAction('correct')"> {{ $t('ui.correctionUnblocking') }} </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>

    <AlertDialog :open="resolutionOpen" @update:open="handleResolutionOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ resolutionAction ? $t(ActionMeta[resolutionAction].title) : '' }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ resolutionAction ? $t(ActionMeta[resolutionAction].description) : '' }}
            {{ $t('ui.affectedTargetTheReasonIsRecordedInCompetitionEventsAnd', { target: resolutionTargetLabel }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <div class="grid gap-2 px-1 pb-2">
          <Textarea
            v-model="resolutionReason"
            :placeholder="$t('ui.pleaseEnterAReasonForTheDispositionOfAtLeast')"
            :aria-label="$t('ui.reasonsForDisposal')"
            aria-describedby="cheat-resolution-reason-help"
            :aria-invalid="remainingCharacters > 0"
            :disabled="resolutionPending"
          />
          <p
            id="cheat-resolution-reason-help"
            class="text-xs"
            :class="remainingCharacters > 0 ? 'text-destructive' : 'text-muted-foreground'"
          >
            <template v-if="remainingCharacters > 0">
              {{ $t('ui.theReasonMustBeAtLeast8CharactersMoreRequired', { count: remainingCharacters }) }}
            </template>
            <template v-else> {{ $t('ui.theLengthOfTheReasonMeetsTheRequirements') }} </template>
          </p>
          <p v-if="resolutionError" role="alert" class="text-destructive text-sm">
            {{ $message(resolutionError) }}
          </p>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="resolutionPending"> {{ $t('ui.cancel') }} </AlertDialogCancel>
          <Button
            type="button"
            :variant="resolutionAction === 'confirm' ? 'destructive' : 'default'"
            :disabled="!canSubmitResolution"
            @click="handleResolutionSubmit"
          >
            <Spinner v-if="resolutionPending" data-icon="inline-start" />
            {{ resolutionPending ? $t('ui.submitting') : (resolutionAction ? $t(ActionMeta[resolutionAction].title) : $t('ui.confirm')) }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
