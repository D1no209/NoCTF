<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformRuntimesPageViewState } from '~/features/routes/admin/platform/useAdminPlatformRuntimesPage'

const viewProps = defineProps<{ state: AdminPlatformRuntimesPageViewState }>()
const { formatCapacityAmount, runnerFailureLabel, ExternalLink, RefreshCw, filters, detailTarget, items, loading, error, initialized, refresh, page, pageCount, total, pageLimit, loadPage, setPageSize, detail, applyFilters, clearFilters, terminateTarget, terminatePending, terminationError, forceTerminateTarget, forceTerminateReason, forceTerminateConfirmed, forceTerminatePending, forceTerminationError, teamLabel, stateBadgeVariant, canTerminate, openTermination, openForceTermination, submitTermination, submitForceTermination, onClickDetailTarget, onUpdateOpenDetailTarget, onUpdateOpenTerminateTarget, onUpdateOpenForceTerminateTarget } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-lg font-semibold">{{ $t('ui.runtimeContainers') }}</h2>
      <Button variant="outline" size="sm" :disabled="loading" @click="refresh">
        <Spinner v-if="loading" data-icon="inline-start" />
        <RefreshCw v-else data-icon="inline-start" />
        {{ $t('ui.refresh') }}
      </Button>
    </div>
    <Card>
      <CardHeader class="sr-only">
        <CardTitle>{{ $t('ui.applyFilters') }}</CardTitle>
        <CardDescription>{{ $t('ui.activeContainersOnlySearchByCompetitionChallengeOrAttributedTeam') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <UiForm @submit.prevent="applyFilters">
          <FieldGroup class="grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(12rem,1fr)_repeat(3,minmax(7rem,9rem))_auto] xl:items-end">
            <Field>
              <FieldLabel for="platform-runtime-search" class="sr-only">{{ $t('ui.searchCompetitionChallengeOrTeam') }}</FieldLabel>
              <Input id="platform-runtime-search" v-model="filters.search" maxlength="200" :placeholder="$t('ui.searchCompetitionChallengeOrTeam')" />
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-scope" class="sr-only">{{ $t('ui.allSources') }}</FieldLabel>
              <Select v-model="filters.scope">
                <SelectTrigger id="platform-runtime-scope" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('ui.allSources') }}</SelectItem>
                  <SelectItem value="Competition">{{ $t('ui.competitionContainers') }}</SelectItem>
                  <SelectItem value="ChallengeTest">{{ $t('ui.challengeTest') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-state" class="sr-only">{{ $t('ui.statusAll') }}</FieldLabel>
              <Select v-model="filters.state">
                <SelectTrigger id="platform-runtime-state" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('ui.statusAll') }}</SelectItem>
                  <SelectItem value="Queued">{{ $t('ui.queuing') }}</SelectItem>
                  <SelectItem value="Provisioning">{{ $t('ui.inPreparation') }}</SelectItem>
                  <SelectItem value="Running">{{ $t('ui.running2') }}</SelectItem>
                  <SelectItem value="Stopping">{{ $t('ui.stopping') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-kind" class="sr-only">{{ $t('ui.typeAll') }}</FieldLabel>
              <Select v-model="filters.kind">
                <SelectTrigger id="platform-runtime-kind" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('ui.typeAll') }}</SelectItem>
                  <SelectItem value="Container">{{ $t('ui.container') }}</SelectItem>
                  <SelectItem value="Compose">{{ $t('ui.compose') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field orientation="horizontal">
              <Button type="submit" size="sm" :disabled="loading">
                <Spinner v-if="loading" data-icon="inline-start" />{{ $t('ui.applyFilters') }}
              </Button>
              <Button type="button" variant="ghost" size="sm" :disabled="loading" @click="clearFilters">{{ $t('ui.clear') }}</Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
    </Card>
    <div class="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground" role="status">
      <span>{{ $t('ui.activeContainersLoaded', { count: items.length }) }}</span>
      <span>{{ $t('ui.thePageRefreshesAutomaticallyEvery10Seconds') }}</span>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-64 w-full" />
    <Empty v-else-if="initialized && items.length === 0 && !error" class="border border-dashed py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.thereAreNoRuntimeInstancesThatMatchTheCriteria') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.queuedProvisioningRunningAndStoppingContainerOrComposeInstancesAppear') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <template v-else-if="items.length > 0">
      <Table class="min-w-[48rem] table-fixed">
        <TableHeader>
          <TableRow>
            <TableHead class="w-[20%]">{{ $t('ui.team') }}</TableHead>
            <TableHead class="w-[29%]">{{ $t('ui.competitionChallenge') }}</TableHead>
            <TableHead class="w-[9%]">{{ $t('ui.type') }}</TableHead>
            <TableHead class="w-[10%]">{{ $t('ui.status') }}</TableHead>
            <TableHead class="w-[17%]">{{ $t('ui.expirationTime') }}</TableHead>
            <TableHead class="w-[15%] text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.runtime?.id">
            <TableCell class="whitespace-normal break-words font-medium">{{ teamLabel(item) }}</TableCell>
            <TableCell class="whitespace-normal break-words">
              <div class="grid gap-1">
                <span>{{ item.challengeTitle ?? '-' }}</span>
                <span class="text-xs text-muted-foreground">{{ item.scope === 'ChallengeTest' ? $t('ui.challengeTest') : item.competitionTitle ?? '-' }}</span>
              </div>
            </TableCell>
            <TableCell class="whitespace-normal break-words">
              {{ enumLabel(RuntimeKindLabel, item.runtime?.runtimeKind) }}
            </TableCell>
            <TableCell>
              <Badge :variant="stateBadgeVariant(item.runtime?.state)">
                {{ enumLabel(RuntimeStateLabel, item.runtime?.state) }}
              </Badge>
            </TableCell>
            <TableCell class="whitespace-normal font-mono text-xs tabular-nums">
              {{ adminFormatDateTime(item.runtime?.expiresAt) }}
            </TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="onClickDetailTarget(item)">{{ $t('ui.details') }}</Button>
                <Button
                  v-if="canTerminate(item)"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
                  @click="openTermination(item)"
                >
                  {{ $t('ui.terminate') }}
                </Button>
                <Button
                  v-if="item.runtime?.canForceTerminate"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
                  @click="openForceTermination(item)"
                >
                  {{ $t('ui.forcedTermination') }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <OffsetPagination
        :page="page"
        :page-count="pageCount"
        :total="total"
        :limit="pageLimit"
        :loading="loading"
        @update:page="loadPage"
        @update:limit="setPageSize"
      />
    </template>

    <Sheet :open="detailTarget !== null" @update:open="onUpdateOpenDetailTarget">
      <SheetContent data-scroll-surface class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('ui.runtimeDetails') }}</SheetTitle>
          <SheetDescription class="break-all font-mono text-xs">{{ detail?.runtime?.id }}</SheetDescription>
        </SheetHeader>
        <div v-if="detail" class="flex flex-col gap-4 px-4 pb-4 text-sm">
          <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3">
            <dt class="text-muted-foreground">{{ $t('ui.team') }}</dt><dd class="break-words">{{ teamLabel(detail) }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.competitionChallenge') }}</dt>
            <dd class="break-words">{{ detail.challengeTitle }}<p class="mt-1 text-xs text-muted-foreground">{{ detail.competitionTitle ?? $t('ui.challengeTest') }}</p></dd>
            <dt class="text-muted-foreground">{{ $t('ui.type') }}</dt><dd>{{ enumLabel(RuntimeKindLabel, detail.runtime?.runtimeKind) }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.status') }}</dt><dd><Badge :variant="stateBadgeVariant(detail.runtime?.state)">{{ enumLabel(RuntimeStateLabel, detail.runtime?.state) }}</Badge></dd>
            <dt class="text-muted-foreground">{{ $t('ui.placement') }}</dt><dd class="break-all">{{ enumLabel(RuntimeProviderLabel, detail.runtime?.provider) }}<p class="mt-1 font-mono text-xs">{{ detail.runtime?.runnerId ?? $t('ui.runnerNotAssigned') }}</p></dd>
            <dt class="text-muted-foreground">{{ $t('ui.creationTime') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.createdAt) }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.expirationTime') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.expiresAt) }}</dd>
          </dl>
          <FieldDescription v-if="detail.runtime?.waitingReason">{{ runnerFailureLabel(detail.runtime.waitingReason) }}</FieldDescription>
          <template v-if="detail.runtime?.capacity?.length">
            <Separator />
            <FieldDescription>{{ $t('capacity.units') }}</FieldDescription>
            <dl v-for="allocation in detail.runtime.capacity" :key="allocation.operationId" class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2">
              <dt>{{ $t('capacity.workload') }}</dt><dd class="break-all font-mono text-xs">{{ allocation.operationId }}</dd>
              <dt>{{ $t('capacity.limit') }}</dt><dd class="font-mono tabular-nums">{{ formatCapacityAmount(allocation.limit) }}</dd>
              <dt>{{ $t('capacity.budget') }}</dt><dd class="font-mono tabular-nums">{{ formatCapacityAmount(allocation.budget) }}</dd>
            </dl>
          </template>
          <template v-if="detail.runtime?.urls?.length">
            <Separator />
            <p class="font-medium">{{ $t('ui.accessEntrance') }}</p>
            <code v-for="url in detail.runtime.urls" :key="url" class="whitespace-pre-wrap break-all text-xs">{{ url }}</code>
          </template>
          <Separator />
          <Button v-if="detail.runtime?.competitionId" variant="outline" as-child>
            <NuxtLink :to="`/admin/competitions/${detail.runtime.competitionId}/runtimes`"><ExternalLink data-icon="inline-start" />{{ $t('ui.competitionRuntimes') }}</NuxtLink>
          </Button>
          <Button v-else-if="detail.runtime?.challengeId" variant="outline" as-child>
            <NuxtLink :to="`/admin/challenges/${detail.runtime.challengeId}`"><ExternalLink data-icon="inline-start" />{{ $t('ui.challengeTemplate') }}</NuxtLink>
          </Button>
        </div>
      </SheetContent>
    </Sheet>

    <AlertDialog
      :open="terminateTarget !== null"
      @update:open="onUpdateOpenTerminateTarget"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.terminateThisRuntimeInstance') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.immediatelyStopAndCleanTheInstanceForOnTheEnvironment', {
              team: terminateTarget ? teamLabel(terminateTarget) : '-',
              challenge: terminateTarget?.challengeTitle ?? '-',
            }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="terminationError" variant="destructive">
          <AlertDescription>{{ $message(terminationError) }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="terminatePending" @click="submitTermination">
            <Spinner v-if="terminatePending" data-icon="inline-start" /> {{ $t('ui.confirmTermination') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog
      :open="forceTerminateTarget !== null"
      @update:open="onUpdateOpenForceTerminateTarget"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.forcefullyTerminateAStuckInstance') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.theRunnerWillCleanUpTheActualResourcesBasedOn') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <FieldGroup>
          <Alert v-if="forceTerminationError" variant="destructive">
            <AlertDescription>{{ $message(forceTerminationError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="platform-force-termination-reason">{{ $t('ui.reasonForOperation') }}</FieldLabel>
            <Textarea
              id="platform-force-termination-reason"
              v-model="forceTerminateReason"
              :disabled="forceTerminatePending"
              maxlength="512"
              :placeholder="$t('ui.atLeast8CharactersUsedToIdentifyThisCleanupOperation')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}{{ $t('ui.512') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="platform-force-termination-confirm" v-model="forceTerminateConfirmed" :disabled="forceTerminatePending" />
            <FieldLabel for="platform-force-termination-confirm" class="font-normal">
              {{ $t('ui.iConfirmThisIsAStuckInstanceAndUnderstandThat') }}
            </FieldLabel>
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
            <Spinner v-if="forceTerminatePending" data-icon="inline-start" /> {{ $t('ui.confirmForcedTermination') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
