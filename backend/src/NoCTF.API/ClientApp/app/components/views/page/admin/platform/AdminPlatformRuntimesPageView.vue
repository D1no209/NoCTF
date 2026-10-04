<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformRuntimesPageViewState } from '~/features/routes/admin/platform/useAdminPlatformRuntimesPage'

const viewProps = defineProps<{ state: AdminPlatformRuntimesPageViewState }>()
const { RuntimeFlagsPanel, adminCompetitionPath, adminRuntimeTeamPath, adminRuntimeChallengePath, adminRuntimePath, detailOpen, detailLoading, detailError, formatCapacityAmount, runnerFailureLabel, ExternalLink, RuntimeAccessUrl, RefreshCw, filters, items, loading, error, initialized, refresh, page, pageCount, total, pageLimit, loadPage, setPageSize, detail, applyFilters, clearFilters, terminateTarget, terminatePending, terminationError, forceTerminateTarget, forceTerminateReason, forceTerminateConfirmed, forceTerminatePending, forceTerminationError, teamLabel, stateBadgeVariant, canTerminate, openTermination, openForceTermination, submitTermination, submitForceTermination, onClickDetailTarget, onUpdateOpenDetailTarget, onUpdateOpenTerminateTarget, onUpdateOpenForceTerminateTarget } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-lg font-semibold">{{ $t('common.label.runtimeContainers') }}</h2>
      <Button variant="outline" size="sm" :disabled="loading" @click="refresh">
        <Spinner v-if="loading" data-icon="inline-start" />
        <RefreshCw v-else data-icon="inline-start" />
        {{ $t('common.label.refresh') }}
      </Button>
    </div>
    <Card>
      <CardHeader class="sr-only">
        <CardTitle>{{ $t('common.label.applyFilters') }}</CardTitle>
        <CardDescription>{{ $t('runtime.platformRuntimes.description.activeContainersSearchCompetition') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <UiForm @submit.prevent="applyFilters">
          <FieldGroup class="grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(12rem,1fr)_repeat(3,minmax(7rem,9rem))_auto] xl:items-end">
            <Field>
              <FieldLabel for="platform-runtime-search" class="sr-only">{{ $t('runtime.platformRuntimes.label.searchCompetitionChallengeTeam') }}</FieldLabel>
              <Input id="platform-runtime-search" v-model="filters.search" maxlength="200" :placeholder="$t('runtime.platformRuntimes.label.searchCompetitionChallengeTeam')" />
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-scope" class="sr-only">{{ $t('runtime.label.sources') }}</FieldLabel>
              <Select v-model="filters.scope">
                <SelectTrigger id="platform-runtime-scope" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('runtime.label.sources') }}</SelectItem>
                  <SelectItem value="Competition">{{ $t('runtime.label.competitionContainers') }}</SelectItem>
                  <SelectItem value="ChallengeTest">{{ $t('runtime.label.challengeTest') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-state" class="sr-only">{{ $t('common.label.status.runtimesPageView') }}</FieldLabel>
              <Select v-model="filters.state">
                <SelectTrigger id="platform-runtime-state" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('common.label.status.runtimesPageView') }}</SelectItem>
                  <SelectItem value="Queued">{{ $t('common.label.queuing') }}</SelectItem>
                  <SelectItem value="Provisioning">{{ $t('runtime.label.preparation') }}</SelectItem>
                  <SelectItem value="Running">{{ $t('common.label.running.adminFormat') }}</SelectItem>
                  <SelectItem value="Stopping">{{ $t('common.label.stopping') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-kind" class="sr-only">{{ $t('common.label.type.runtimesPageView') }}</FieldLabel>
              <Select v-model="filters.kind">
                <SelectTrigger id="platform-runtime-kind" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('common.label.type.runtimesPageView') }}</SelectItem>
                  <SelectItem value="Container">{{ $t('runtime.label.container') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field orientation="horizontal">
              <Button type="submit" size="sm" :disabled="loading">
                <Spinner v-if="loading" data-icon="inline-start" />{{ $t('common.label.applyFilters') }}
              </Button>
              <Button type="button" variant="ghost" size="sm" :disabled="loading" @click="clearFilters">{{ $t('common.label.clear') }}</Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
    </Card>
    <div class="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground" role="status">
      <span>{{ $t('runtime.label.activeContainersLoaded', { count: items.length }) }}</span>
      <span>{{ $t('runtime.platformRuntimes.description.pageRefreshesAutomaticallyEvery') }}</span>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-64 w-full" />
    <Empty v-else-if="initialized && items.length === 0 && !error" class="border border-dashed py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('runtime.platformRuntimes.description.thereRuntimeInstancesMatch') }}</EmptyTitle>
        <EmptyDescription>{{ $t('runtime.platformRuntimes.description.queuedProvisioningRunningStopping') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <template v-else-if="items.length > 0">
      <Table class="min-w-[48rem] table-fixed">
        <TableHeader>
          <TableRow>
            <TableHead class="w-[20%]">{{ $t('common.label.team') }}</TableHead>
            <TableHead class="w-[29%]">{{ $t('runtime.label.competitionChallenge') }}</TableHead>
            <TableHead class="w-[9%]">{{ $t('common.label.type') }}</TableHead>
            <TableHead class="w-[10%]">{{ $t('common.label.status') }}</TableHead>
            <TableHead class="w-[17%]">{{ $t('runtime.label.expirationTime') }}</TableHead>
            <TableHead class="w-[15%] text-right">{{ $t('common.label.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="(item.runtime?.id) ?? undefined">
            <TableCell class="whitespace-normal break-words font-medium"><NuxtLink v-if="adminRuntimeTeamPath(item.runtime)" :to="adminRuntimeTeamPath(item.runtime)" class="hover:underline">{{ teamLabel(item) }}</NuxtLink><span v-else>{{ teamLabel(item) }}</span></TableCell>
            <TableCell class="whitespace-normal break-words">
              <div class="grid gap-1">
                <NuxtLink v-if="adminRuntimeChallengePath(item.runtime)" :to="adminRuntimeChallengePath(item.runtime)" class="hover:underline">{{ item.challengeTitle ?? '-' }}</NuxtLink><span v-else>{{ item.challengeTitle ?? '-' }}</span>
                <NuxtLink v-if="item.runtime?.competitionId" :to="adminCompetitionPath(item.runtime.competitionId)" class="text-xs text-muted-foreground hover:underline">{{ item.competitionTitle ?? item.runtime.competitionId }}</NuxtLink><span v-else class="text-xs text-muted-foreground">{{ $t('runtime.label.challengeTest') }}</span>
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
                <Button variant="ghost" size="sm" @click="onClickDetailTarget(item)">{{ $t('common.label.details') }}</Button>
                <Button
                  v-if="canTerminate(item)"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
                  @click="openTermination(item)"
                >
                  {{ $t('runtime.label.terminate') }}
                </Button>
                <Button
                  v-if="item.runtime?.canForceTerminate"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
                  @click="openForceTermination(item)"
                >
                  {{ $t('runtime.label.forcedTermination') }}
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

    <Sheet :open="detailOpen" @update:open="onUpdateOpenDetailTarget">
      <SheetContent data-scroll-surface class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('runtime.label.runtimeDetails') }}</SheetTitle>
          <SheetDescription class="break-all font-mono text-xs">{{ detail?.runtime?.id }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <Alert v-else-if="detailError" variant="destructive"><AlertDescription>{{ $message(detailError) }}</AlertDescription></Alert>
        <div v-else-if="detail" class="flex flex-col gap-4 px-4 pb-4 text-sm">
          <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3">
            <dt class="text-muted-foreground">{{ $t('common.label.team') }}</dt><dd class="break-words"><NuxtLink v-if="adminRuntimeTeamPath(detail.runtime)" :to="adminRuntimeTeamPath(detail.runtime)" class="hover:underline">{{ teamLabel(detail) }}</NuxtLink><span v-else>{{ teamLabel(detail) }}</span></dd>
            <dt class="text-muted-foreground">{{ $t('runtime.label.competitionChallenge') }}</dt>
            <dd class="break-words"><NuxtLink v-if="adminRuntimeChallengePath(detail.runtime)" :to="adminRuntimeChallengePath(detail.runtime)" class="hover:underline">{{ detail.challengeTitle }}</NuxtLink><p class="mt-1 text-xs text-muted-foreground"><NuxtLink v-if="detail.runtime?.competitionId" :to="adminCompetitionPath(detail.runtime.competitionId)" class="hover:underline">{{ detail.competitionTitle }}</NuxtLink><span v-else>{{ $t('runtime.label.challengeTest') }}</span></p></dd>
            <dt class="text-muted-foreground">{{ $t('common.label.type') }}</dt><dd>{{ enumLabel(RuntimeKindLabel, detail.runtime?.runtimeKind) }}</dd>
            <dt class="text-muted-foreground">{{ $t('common.label.status') }}</dt><dd><Badge :variant="stateBadgeVariant(detail.runtime?.state)">{{ enumLabel(RuntimeStateLabel, detail.runtime?.state) }}</Badge></dd>
            <dt class="text-muted-foreground">{{ $t('runtime.label.placement') }}</dt><dd class="break-all">{{ enumLabel(RuntimeProviderLabel, detail.runtime?.provider) }}<p class="mt-1 font-mono text-xs">{{ detail.runtime?.runnerId ?? $t('runtime.label.runnerAssigned') }}</p></dd>
            <dt class="text-muted-foreground">{{ $t('common.label.creationTime') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.createdAt) }}</dd>
            <dt class="text-muted-foreground">{{ $t('runtime.label.expirationTime') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.expiresAt) }}</dd>
          </dl>
          <FieldDescription v-if="detail.runtime?.waitingReason">{{ runnerFailureLabel(detail.runtime.waitingReason) }}</FieldDescription>
          <template v-if="detail.runtime?.capacity?.length">
            <Separator />
            <FieldDescription>{{ $t('capacity.units') }}</FieldDescription>
            <dl v-for="allocation in detail.runtime.capacity" :key="allocation.operationId ?? undefined" class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2">
              <dt>{{ $t('capacity.workload') }}</dt><dd class="break-all font-mono text-xs">{{ allocation.operationId }}</dd>
              <dt>{{ $t('capacity.limit') }}</dt><dd class="font-mono tabular-nums">{{ formatCapacityAmount(allocation.limit) }}</dd>
              <dt>{{ $t('capacity.budget') }}</dt><dd class="font-mono tabular-nums">{{ formatCapacityAmount(allocation.budget) }}</dd>
            </dl>
          </template>
          <template v-if="detail.runtime?.accesses?.length">
            <Separator />
            <p class="font-medium">{{ $t('runtime.label.accessEntrance') }}</p>
            <component :is="RuntimeAccessUrl" v-for="access in detail.runtime.accesses" :key="`${access.directAddress}:${access.webSocketAddress}`" :access="access" />
          </template>
          <Separator />
          <component :is="RuntimeFlagsPanel" v-if="detail.runtime?.id" :key="detail.runtime.id ?? undefined" :runtime-id="detail.runtime.id" />
          <Button v-if="detail.runtime?.competitionId" variant="outline" as-child>
            <NuxtLink :to="adminRuntimePath(detail.runtime.competitionId, detail.runtime.id)"><ExternalLink data-icon="inline-start" />{{ $t('runtime.label.competitionRuntimes') }}</NuxtLink>
          </Button>
          <Button v-else-if="detail.runtime?.challengeId" variant="outline" as-child>
            <NuxtLink :to="`/admin/challenges/${detail.runtime.challengeId}`"><ExternalLink data-icon="inline-start" />{{ $t('runtime.label.challengeTemplate') }}</NuxtLink>
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
          <AlertDialogTitle>{{ $t('runtime.label.terminateRuntimeInstance') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('runtime.platformRuntimes.description.immediatelyStopCleanInstance', {
              team: terminateTarget ? teamLabel(terminateTarget) : '-',
              challenge: terminateTarget?.challengeTitle ?? '-',
            }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="terminationError" variant="destructive">
          <AlertDescription>{{ $message(terminationError) }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="terminatePending" @click="submitTermination">
            <Spinner v-if="terminatePending" data-icon="inline-start" /> {{ $t('runtime.label.confirmTermination') }}
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
          <AlertDialogTitle>{{ $t('runtime.platformRuntimes.label.forcefullyTerminateStuckInstance') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('runtime.platformRuntimes.description.runnerCleanActualResources') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <FieldGroup>
          <Alert v-if="forceTerminationError" variant="destructive">
            <AlertDescription>{{ $message(forceTerminationError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="platform-force-termination-reason">{{ $t('runtime.label.reason') }}</FieldLabel>
            <Textarea
              id="platform-force-termination-reason"
              v-model="forceTerminateReason"
              :disabled="forceTerminatePending"
              maxlength="512"
              :placeholder="$t('runtime.platformRuntimes.description.leastCharactersIdentifyCleanup')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}{{ $t('runtime.terminate.reasonLimitSuffix') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="platform-force-termination-confirm" v-model="forceTerminateConfirmed" :disabled="forceTerminatePending" />
            <FieldLabel for="platform-force-termination-confirm" class="font-normal">
              {{ $t('runtime.platformRuntimes.description.iConfirmStuckInstance') }}
            </FieldLabel>
          </Field>
        </FieldGroup>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="forceTerminatePending">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="forceTerminatePending || !forceTerminateConfirmed || forceTerminateReason.trim().length < 8"
            @click="submitForceTermination"
          >
            <Spinner v-if="forceTerminatePending" data-icon="inline-start" /> {{ $t('runtime.label.confirmForcedTermination') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
