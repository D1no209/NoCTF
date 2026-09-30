<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdRuntimesPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdRuntimesPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdRuntimesPageViewState }>()
const { canWrite, isAdministrator, challengeOptions, teamOptions, runtimeTeamLabel, isPlayerManagedRuntime, challengeTitle, filterChallenge, filterTeam, filterState, filterKind, items, loading, listError, initialized, page, pageCount, total, pageLimit, loadPage, setPageSize, applyFilters, detail, detailOpen, detailLoading, openDetail, opMessage, isRuntimePending, isRuntimeOperationPending, runRuntimeOp, terminateDialog, terminatePending, canTerminate, submitTermination, forceTerminateDialog, forceTerminateReason, forceTerminateConfirmed, forceTerminatePending, openForceTermination, submitForceTermination, extendDialog, extendSeconds, extendPending, canExtendRuntime, renewalHint, submitExtend, RuntimeAccessUrl, onClickFilterChallenge, onClickTerminateDialog, onClickExtendDialog, onUpdateOpenExtendDialog, onClickExtendDialog2, onUpdateOpenTerminateDialog, onUpdateOpenForceTerminateDialog } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardContent class="flex flex-wrap items-center gap-3 pt-6">
        <Select v-model="filterChallenge">
          <SelectTrigger class="w-48">
            <SelectValue :placeholder="$t('ui.topicsAll')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterTeam">
          <SelectTrigger class="w-48">
            <SelectValue :placeholder="$t('ui.teamAll')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterState">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('ui.statusAll')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Queued">{{ $t('ui.queuing') }}</SelectItem>
              <SelectItem value="Provisioning">{{ $t('ui.inPreparation') }}</SelectItem>
              <SelectItem value="Running">{{ $t('ui.running2') }}</SelectItem>
              <SelectItem value="Stopping">{{ $t('ui.stopping') }}</SelectItem>
              <SelectItem value="Stopped">{{ $t('ui.stopped') }}</SelectItem>
              <SelectItem value="Failed">{{ $t('ui.failed') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Select v-model="filterKind">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('ui.typeAll')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Container">{{ $t('ui.container') }}</SelectItem>
              <SelectItem value="OvaVm">{{ $t('ui.virtualMachine') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Button size="sm" @click="applyFilters">{{ $t('ui.applyFilters') }}</Button>
        <Button variant="ghost" size="sm" @click="onClickFilterChallenge"> {{ $t('ui.clear') }} </Button>
      </CardContent>
    </Card>

    <Alert v-if="opMessage">
      <AlertDescription>{{ opMessage }}</AlertDescription>
    </Alert>

    <Alert v-if="listError" variant="destructive">
      <AlertDescription>{{ $message(listError.message) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.thereAreNoRuntimeInstancesThatMatchTheCriteria') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('ui.team') }}</TableHead>
            <TableHead>{{ $t('ui.challenge') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.type') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.status') }}</TableHead>
            <TableHead class="w-44">{{ $t('ui.expirationTime') }}</TableHead>
            <TableHead class="w-64 text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="rt in items" :key="rt.id">
            <TableCell class="font-medium">{{ runtimeTeamLabel(rt) }}</TableCell>
            <TableCell>{{ challengeTitle(rt.competitionChallengeId) }}</TableCell>
            <TableCell>{{ enumLabel(RuntimeKindLabel, rt.runtimeKind) }}</TableCell>
            <TableCell>
              <Badge :variant="rt.state === 'Running' ? 'default' : rt.state === 'Failed' ? 'destructive' : 'secondary'">
                {{ enumLabel(RuntimeStateLabel, rt.state) }}
              </Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(rt.expiresAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(rt.id)">{{ $t('ui.details') }}</Button>
                <template v-if="canWrite">
                  <Button
                    v-if="isPlayerManagedRuntime(rt) && (rt.state === 'Stopped' || rt.state === 'Failed')"
                    variant="ghost" size="sm" :disabled="isRuntimePending(rt)"
                    @click="runRuntimeOp(rt, 'start')"
                  >
                    <Spinner v-if="isRuntimeOperationPending(rt, 'start')" data-icon="inline-start" /> {{ $t('ui.start') }} </Button>
                  <Button
                    v-if="canTerminate(rt)"
                    variant="destructive" size="sm" :disabled="isRuntimePending(rt)"
                    @click="onClickTerminateDialog(rt)"
                  >{{ $t('ui.terminate') }}</Button>
                  <Button
                    v-if="isAdministrator && rt.canForceTerminate"
                    variant="destructive" size="sm"
                    :disabled="isRuntimePending(rt)"
                    @click="openForceTermination(rt)"
                  >{{ $t('ui.forcedTermination') }}</Button>
                  <Button
                    v-if="isPlayerManagedRuntime(rt)"
                    variant="ghost" size="sm" :disabled="isRuntimePending(rt)"
                    @click="runRuntimeOp(rt, 'reset')"
                  >
                    <Spinner v-if="isRuntimeOperationPending(rt, 'reset')" data-icon="inline-start" /> {{ $t('ui.reset') }} </Button>
                  <Hint v-if="isPlayerManagedRuntime(rt) && rt.teamId && rt.state === 'Running'" :content="renewalHint(rt)">
                    <span :tabindex="canExtendRuntime(rt) ? -1 : 0">
                      <Button variant="ghost" size="sm"
                        :disabled="isRuntimePending(rt) || !canExtendRuntime(rt)"
                        @click="onClickExtendDialog(rt)">{{ $t('ui.renew') }}</Button>
                    </span>
                  </Hint>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </template>
    <OffsetPagination v-if="initialized" :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading" @update:page="loadPage" @update:limit="setPageSize" />

    <Sheet v-model:open="detailOpen">
      <SheetContent data-scroll-surface class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('ui.runtimeDetails') }}</SheetTitle>
          <SheetDescription class="font-mono text-xs break-all">{{ detail?.id }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.team') }}</span><span>{{ runtimeTeamLabel(detail) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.challenge') }}</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.type') }}</span><span>{{ enumLabel(RuntimeKindLabel, detail.runtimeKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.provider') }}</span><span>{{ enumLabel(RuntimeProviderLabel, detail.provider) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.runner') }}</span><span>{{ detail.runnerId ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.status') }}</span><span>{{ enumLabel(RuntimeStateLabel, detail.state) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.failureCode') }}</span><span>{{ detail.failureCode ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.creationTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.createdAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.runningTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.runningAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.expirationTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.expiresAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.stopTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.stoppedAt) }}</span></div>
          <template v-if="detail.accesses?.length">
            <Separator />
            <p class="text-muted-foreground">{{ $t('ui.accessAddress') }}</p>
            <component :is="RuntimeAccessUrl" v-for="access in detail.accesses" :key="`${access.directAddress}:${access.webSocketAddress}`" :access="access" />
          </template>
          <template v-if="detail.publishedPorts?.length">
            <Separator />
            <p class="text-muted-foreground">{{ $t('ui.portMapping') }}</p>
            <div v-for="(p, i) in detail.publishedPorts" :key="i" class="font-mono text-xs">
              {{ $t('ui.host2', { service: p.serviceName ?? $t('ui.service2'), containerPort: p.containerPort ?? '-', hostPort: p.hostPort ?? '-' }) }}
            </div>
          </template>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="extendDialog !== null" @update:open="onUpdateOpenExtendDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.extendedRunTime') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.extendTheRuntimeExpirationFor', { team: runtimeTeamLabel(extendDialog) }) }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="extend-seconds">{{ $t('ui.extendSeconds') }}</FieldLabel>
            <NumberInput id="extend-seconds" v-model.number="extendSeconds"  min="60" step="60" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickExtendDialog2(null)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="extendPending || !extendDialog || !canExtendRuntime(extendDialog)" @click="submitExtend">
            <Spinner v-if="extendPending" data-icon="inline-start" /> {{ $t('ui.confirmRenewal') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="terminateDialog !== null" @update:open="onUpdateOpenTerminateDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.terminateThisRuntimeInstance') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.immediatelyStopAndCleanTheInstanceForOnTheEnvironment', {
              team: runtimeTeamLabel(terminateDialog),
              challenge: challengeTitle(terminateDialog?.competitionChallengeId),
            }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="terminatePending"
            @click="submitTermination"
          >
            <Spinner v-if="terminatePending" data-icon="inline-start" /> {{ $t('ui.confirmTermination') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog
      :open="forceTerminateDialog !== null"
      @update:open="onUpdateOpenForceTerminateDialog"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.forcefullyTerminateAStuckInstance') }}</AlertDialogTitle>
          <AlertDialogDescription> {{ $t('ui.theRunnerWillCleanUpTheActualResourcesBasedOn') }} </AlertDialogDescription>
        </AlertDialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="force-termination-reason">{{ $t('ui.reasonForOperation') }}</FieldLabel>
            <Textarea
              id="force-termination-reason"
              v-model="forceTerminateReason"
              maxlength="512"
              :placeholder="$t('ui.atLeast8CharactersWillBeWrittenToImmutableMatch')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}{{ $t('ui.512') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="force-termination-confirm" v-model="forceTerminateConfirmed" />
            <FieldLabel for="force-termination-confirm" class="font-normal"> {{ $t('ui.iConfirmThisIsAStuckInstanceAndUnderstandThat') }} </FieldLabel>
          </Field>
        </FieldGroup>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="forceTerminatePending">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="forceTerminatePending || !forceTerminateConfirmed || forceTerminateReason.trim().length < 8"
            @click="submitForceTermination"
          >
            <Spinner v-if="forceTerminatePending" data-icon="inline-start" /> {{ $t('ui.confirmForcedTermination') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
