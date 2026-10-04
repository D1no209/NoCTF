<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdCheatsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdCheatsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdCheatsPageViewState }>()
const { adminUserPath, adminTeamPath, adminChallengePath, competitionId, filterStatus, filterFrom, filterTo, filterError, pendingCount, items, loading, listError, hasMore, initialized, loadNextPage, applyFilters, detail, detailOpen, detailLoading, showFlag, openDetail, ActionMeta, resolutionAction, canSubmitResolution, resolutionError, resolutionOpen, resolutionPending, resolutionReason, remainingCharacters, resolutionTargetLabel, openAction, handleResolutionSubmit, handleResolutionOpen, onClickShowFlag } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <Select v-model="filterStatus">
        <SelectTrigger class="w-40">
          <SelectValue :placeholder="$t('common.label.status.runtimesPageView')" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="All">{{ $t('administration.label.platformLogs') }}</SelectItem>
            <SelectItem value="Pending">{{ $t('administration.label.pending') }}</SelectItem>
            <SelectItem value="Confirmed">{{ $t('administration.label.confirmed') }}</SelectItem>
            <SelectItem value="Dismissed">{{ $t('common.label.dismissed') }}</SelectItem>
            <SelectItem value="Superseded">{{ $t('administration.label.superseded') }}</SelectItem>
            <SelectItem value="Corrected">{{ $t('administration.label.corrected') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <DateTimePicker v-model="filterFrom"  class="w-52" :aria-label="$t('administration.label.startTime')" />
      <span class="text-sm text-muted-foreground">{{ $t('administration.label.byId') }}</span>
      <DateTimePicker v-model="filterTo"  class="w-52" :aria-label="$t('common.label.endTime')" />
      <Button size="sm" @click="applyFilters">{{ $t('common.label.applyFilters') }}</Button>
      <Badge v-if="pendingCount !== null && pendingCount > 0" variant="destructive">
        {{ $t('administration.label.pending.cheatsPageView', { count: pendingCount }) }}
      </Badge>
    </div>

    <p class="text-xs text-muted-foreground"> {{ $t('administration.competitionsBy.description.timesLeftBlankLast') }} </p>

    <Alert v-if="filterError || listError" variant="destructive">
      <AlertDescription>{{ filterError ?? listError?.message }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('administration.label.cheatingIncidentsYet') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('administration.label.sourceTeam') }}</TableHead>
            <TableHead>{{ $t('administration.label.flagOwnerTeam') }}</TableHead>
            <TableHead>{{ $t('common.label.challenge.pageTitle') }}</TableHead>
            <TableHead class="w-20">{{ $t('common.label.type') }}</TableHead>
            <TableHead class="w-24">{{ $t('common.label.status') }}</TableHead>
            <TableHead class="w-44">{{ $t('administration.label.detectionTime') }}</TableHead>
            <TableHead class="w-40 text-right">{{ $t('common.label.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="i in items" :key="i.gameplayFactId ?? undefined">
            <TableCell class="font-medium">
              <NuxtLink :to="adminTeamPath(competitionId, i.sourceTeamId)" class="hover:underline">{{ i.sourceTeamName }}</NuxtLink>
              <Badge v-if="i.sourceTeamIsBanned" variant="destructive" class="ml-1">{{ $t('common.label.banned') }}</Badge>
            </TableCell>
            <TableCell><NuxtLink v-if="i.ownerTeamId" :to="adminTeamPath(competitionId, i.ownerTeamId)" class="hover:underline">{{ i.ownerTeamName }}</NuxtLink><span v-else>{{ i.ownerTeamName ?? $t('administration.label.multipleTeamsUndetermined') }}</span></TableCell>
            <TableCell><NuxtLink :to="adminChallengePath(competitionId, i.competitionChallengeId)" class="hover:underline">{{ i.challengeTitle }}</NuxtLink></TableCell>
            <TableCell>{{ enumLabel(GameplayFactKindLabel, i.gameplayFactKind) }}</TableCell>
            <TableCell>
              <Badge :variant="i.status === 'Pending' ? 'secondary' : i.status === 'Confirmed' ? 'destructive' : 'outline'">
                {{ enumLabel(CheatIncidentStatusLabel, i.status) }}
              </Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(i.detectedAt) }}</TableCell>
            <TableCell class="text-right">
              <Button variant="ghost" size="sm" @click="openDetail(i.gameplayFactId)">{{ $t('common.label.details') }}</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadNextPage">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('common.label.load') }} </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent data-scroll-surface class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('administration.label.cheatIncidentDetails') }}</SheetTitle>
          <SheetDescription>{{ $t('administration.label.eventId', { id: detail?.gameplayFactId ?? '-' }) }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.sourceTeam') }}</span><NuxtLink v-if="detail.sourceTeamId" :to="adminTeamPath(competitionId, detail.sourceTeamId)" class="hover:underline">{{ detail.sourceTeamName }}</NuxtLink><span v-else>{{ detail.sourceTeamName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.flagOwner') }}</span><NuxtLink v-if="detail.ownerTeamId" :to="adminTeamPath(competitionId, detail.ownerTeamId)" class="hover:underline">{{ detail.ownerTeamName ?? $t('administration.label.multipleTeamsUndetermined') }}</NuxtLink><span v-else>{{ detail.ownerTeamName ?? $t('administration.label.multipleTeamsUndetermined') }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('common.label.challenge.pageTitle') }}</span><NuxtLink v-if="detail.competitionChallengeId" :to="adminChallengePath(competitionId, detail.competitionChallengeId)" class="hover:underline">{{ detail.challengeTitle }}</NuxtLink><span v-else>{{ detail.challengeTitle }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.author') }}</span><NuxtLink v-if="detail.actorUserId" :to="adminUserPath(detail.actorUserId)" class="hover:underline">{{ detail.submittedByUserName }}</NuxtLink><span v-else>{{ detail.submittedByUserName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('common.label.type') }}</span><span>{{ enumLabel(GameplayFactKindLabel, detail.gameplayFactKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('common.label.result') }}</span><span>{{ enumLabel(GameplayFactResultLabel, detail.result) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('common.label.status') }}</span><span>{{ enumLabel(CheatIncidentStatusLabel, detail.status) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('common.label.submissionTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.submittedAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.detectionTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.detectedAt) }}</span></div>
          <template v-if="detail.resolvedByUserName">
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.processor') }}</span><NuxtLink v-if="detail.resolvedByUserId" :to="adminUserPath(detail.resolvedByUserId)" class="hover:underline">{{ detail.resolvedByUserName }}</NuxtLink><span v-else>{{ detail.resolvedByUserName }}</span></div>
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.processingTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.resolvedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">{{ $t('administration.label.reasonsProcessing') }}</span><span class="whitespace-pre-wrap">{{ detail.resolutionReason }}</span></div>
          </template>
          <template v-if="detail.sourceTeamIsBanned">
            <Separator />
            <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('administration.label.banTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.sourceTeamBannedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">{{ $t('administration.label.banReason') }}</span><span class="whitespace-pre-wrap">{{ detail.sourceTeamBanReason ?? '-' }}</span></div>
          </template>
          <Separator />
          <div class="flex flex-col gap-2">
            <span class="text-muted-foreground">{{ $t('administration.label.submittedFlagEvidence') }}</span>
            <Button variant="outline" size="sm" class="w-fit" @click="onClickShowFlag">
              {{ showFlag ? $t('administration.label.hide') : $t('administration.label.showFullFlag') }}
            </Button>
            <div v-if="showFlag" class="rounded-md border bg-muted p-3 font-mono text-xs break-all">
              {{ detail.value }}
            </div>
          </div>
          <div v-if="detail.canConfirm || detail.canDismiss || detail.canCorrect" class="flex flex-wrap gap-2 pt-2">
            <Button v-if="detail.canConfirm" variant="destructive" size="sm" @click="openAction('confirm')"> {{ $t('administration.label.confirmCheatingBan') }} </Button>
            <Button v-if="detail.canDismiss" variant="outline" size="sm" @click="openAction('dismiss')"> {{ $t('administration.label.dismiss') }} </Button>
            <Button v-if="detail.canCorrect" variant="outline" size="sm" @click="openAction('correct')"> {{ $t('administration.label.correctionUnblocking') }} </Button>
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
            {{ $t('administration.competitionsBy.description.affectedTargetReasonRecorded', { target: resolutionTargetLabel }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <div class="grid gap-2 px-1 pb-2">
          <Textarea
            v-model="resolutionReason"
            :placeholder="$t('administration.competitionsBy.description.enterReasonDispositionLeast')"
            :aria-label="$t('administration.label.reasonsDisposal')"
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
              {{ $t('administration.competitionsBy.validation.reasonLeastRequired', { count: remainingCharacters }) }}
            </template>
            <template v-else> {{ $t('administration.competitionsBy.description.lengthReasonMeetsRequirements') }} </template>
          </p>
          <p v-if="resolutionError" role="alert" class="text-destructive text-sm">
            {{ $message(resolutionError) }}
          </p>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="resolutionPending"> {{ $t('common.action.cancel') }} </AlertDialogCancel>
          <Button
            type="button"
            :variant="resolutionAction === 'confirm' ? 'destructive' : 'default'"
            :disabled="!canSubmitResolution"
            @click="handleResolutionSubmit"
          >
            <Spinner v-if="resolutionPending" data-icon="inline-start" />
            {{ resolutionPending ? $t('administration.label.submitting') : (resolutionAction ? $t(ActionMeta[resolutionAction].title) : $t('common.label.confirm')) }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
