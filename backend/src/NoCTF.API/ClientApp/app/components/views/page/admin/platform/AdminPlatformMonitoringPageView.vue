<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformMonitoringPageViewState } from '~/features/routes/admin/platform/useAdminPlatformMonitoringPage'

const viewProps = defineProps<{ state: AdminPlatformMonitoringPageViewState }>()
const { Activity, ExternalLink, RefreshCw, GROUPS, snapshot, loading, error, statusLabel, statusVariant, metricByKind, metricLabel, formatMetric, formatCapturedAt, refreshMonitoring, LATENCY_LABELS, RESOURCE_LABELS } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <div class="flex flex-wrap items-start justify-between gap-4 border-b pb-5">
      <div class="flex min-w-0 items-start gap-3">
        <div class="flex size-10 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
          <Activity class="size-5" />
        </div>
        <div>
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="text-display text-xl">{{ $t('ui.monitoring') }}</h2>
            <Badge :variant="statusVariant(snapshot?.status)">
              {{ statusLabel(snapshot?.status) }}
            </Badge>
          </div>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ $t('ui.aCuratedSummaryOfCriticalPlatformPathsAndCapacity') }}
          </p>
          <div v-if="snapshot" class="mt-2 flex flex-wrap gap-2">
            <Badge :variant="snapshot.prometheusAvailable ? 'default' : 'destructive'">
              {{ $t('ui.prometheus') }} {{ $t(snapshot.prometheusAvailable ? 'ui.available' : 'ui.unavailable2') }}
            </Badge>
            <Badge :variant="snapshot.natsAvailable ? 'default' : 'destructive'">
              {{ $t('ui.nats') }} {{ $t(snapshot.natsAvailable ? 'ui.available' : 'ui.unavailable2') }}
            </Badge>
          </div>
        </div>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button v-if="snapshot?.dashboardUrl" variant="outline" as-child>
          <ExternalLink :href="snapshot.dashboardUrl" target="_blank" rel="noopener noreferrer">
            <ExternalLink data-icon="inline-start" />
            {{ $t('ui.openGrafana') }}
          </ExternalLink>
        </Button>
        <Button variant="outline" :disabled="loading" @click="refreshMonitoring">
          <Spinner v-if="loading" data-icon="inline-start" />
          <RefreshCw v-else data-icon="inline-start" />
          {{ $t('ui.refresh') }}
        </Button>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <Alert v-if="snapshot && !snapshot.prometheusAvailable" variant="destructive">
      <AlertDescription>
        {{ $t('ui.prometheusIsUnavailableThisMonitoringSummaryIsNotALive') }}
      </AlertDescription>
    </Alert>

    <Alert v-else-if="snapshot && !snapshot.natsAvailable" variant="destructive">
      <AlertDescription>
        {{ $t('ui.theNatsExporterOrNatsMonitoringEndpointIsUnavailableMessaging') }}
      </AlertDescription>
    </Alert>

    <div v-if="!snapshot && loading" class="grid gap-4 md:grid-cols-2">
      <Card v-for="index in 4" :key="index">
        <CardHeader><Skeleton class="h-6 w-32" /></CardHeader>
        <CardContent class="grid gap-3 sm:grid-cols-2">
          <Skeleton v-for="item in 4" :key="item" class="h-20 w-full" />
        </CardContent>
      </Card>
    </div>

    <div v-else-if="snapshot" class="grid gap-4 lg:grid-cols-2">
      <section v-for="group in GROUPS" :key="group.title" class="border-t pt-4">
        <h3 class="mb-3 text-sm font-semibold">{{ $t(group.title) }}</h3>
        <div class="grid gap-x-5 sm:grid-cols-2">
          <div
            v-for="kind in group.kinds"
            :key="kind"
            class="flex min-h-20 items-center justify-between gap-3 border-b py-3"
          >
            <div class="min-w-0">
              <p class="text-sm text-muted-foreground">{{ $t(metricLabel(kind)) }}</p>
              <p class="mt-1 font-mono text-xl font-semibold tabular-nums">
                {{ formatMetric(metricByKind(kind)) }}
              </p>
              <p v-if="metricByKind(kind)?.windowSeconds" class="mt-1 text-xs text-muted-foreground">
                {{ $t('ui.samplesInTheLastFiveMinutes') }}：{{ monitoringNumber(metricByKind(kind)?.sampleCount, 0) }}
                · {{ $t('ui.minimumSamples') }} {{ metricByKind(kind)?.minimumSamples }}
              </p>
            </div>
            <Badge :variant="statusVariant(metricByKind(kind)?.status)">
              {{ statusLabel(metricByKind(kind)?.status) }}
            </Badge>
          </div>
        </div>
      </section>
    </div>

    <section v-if="snapshot" class="flex flex-col gap-3">
      <h3 class="text-sm font-semibold">{{ $t('ui.requestAndOperationDurationDetails') }}</h3>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('ui.quantilesMeansSamplesRatesAndErrorsShareTheSameFive') }}
        {{ $t('ui.latencyAlertsRequireMinimumSamplesAndASustainedThresholdBreach') }}：{{ snapshot.latencySustainedWindowMinutes }}。
      </p>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('ui.ordinaryApiExcludesStaticAssetsHealthChecksSignalrAndFile') }}
      </p>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('ui.aPlatformRedisOperationMayContainMultipleCallsNotA') }}
      </p>
      <Table>
        <TableCaption>{{ $t('ui.errorsHttp5xxResponsesFailedPlatformRedisOperations') }}</TableCaption>
        <TableHeader><TableRow>
          <TableHead>{{ $t('ui.categoryEndpoint') }}</TableHead><TableHead>{{ $t('ui.p952') }}</TableHead><TableHead>{{ $t('ui.p99') }}</TableHead>
          <TableHead>{{ $t('ui.mean') }}</TableHead><TableHead>{{ $t('ui.samplesInTheLastFiveMinutes') }}</TableHead>
          <TableHead>{{ $t('ui.rate') }}</TableHead><TableHead>{{ $t('ui.errorRate') }}</TableHead><TableHead>{{ $t('ui.status') }}</TableHead>
        </TableRow></TableHeader>
        <TableBody>
          <TableRow v-for="row in snapshot.latencyDetails" :key="`${row.kind}:${row.endpoint}`">
            <TableCell>
              {{ $t(LATENCY_LABELS[row.kind ?? 0] ?? $t('ui.message10')) }}
              <span v-if="row.endpoint" class="block font-mono text-xs text-muted-foreground">{{ row.endpoint }}</span>
            </TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.p95Milliseconds) }}</TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.p99Milliseconds) }}</TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.meanMilliseconds) }}</TableCell>
            <TableCell class="font-mono">
              {{ monitoringNumber(row.sampleCount, 0) }}
              <span class="block text-xs text-muted-foreground">{{ $t('ui.minimumSamples') }} {{ row.minimumSamples }}</span>
            </TableCell>
            <TableCell class="font-mono">{{ monitoringNumber(row.requestsPerSecond, 2, ' /s') }}</TableCell>
            <TableCell class="font-mono">{{ monitoringNumber(row.errorPercent, 2, '%') }}</TableCell>
            <TableCell><Badge :variant="statusVariant(row.status)">{{ statusLabel(row.status) }}</Badge></TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <p v-if="!snapshot.latencyDetails?.some(row => row.kind === 4)" class="text-sm text-muted-foreground">
        {{ $t('ui.redisOperationDetailsHaveNoSamplesOrAreUnavailableCheck') }}
      </p>
    </section>

    <section v-if="snapshot" class="flex flex-col gap-3">
      <h3 class="text-sm font-semibold">{{ $t('ui.remainingResourcePoolQuotas') }}</h3>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('ui.theMinimumIsTakenAcrossEachPoolSAggregatedAvailable') }}
        {{ $t('ui.onlyOnlineRunnersAreAggregatedANodeGoingOfflineMay') }}
      </p>
      <Table>
        <TableHeader><TableRow>
          <TableHead>{{ $t('ui.resourcePool') }}</TableHead><TableHead>{{ $t('ui.resource') }}</TableHead>
          <TableHead>{{ $t('ui.availableTotalQuota') }}</TableHead><TableHead>{{ $t('ui.remainingRatio') }}</TableHead><TableHead>{{ $t('ui.onlineRunners') }}</TableHead>
        </TableRow></TableHeader>
        <TableBody><TableRow v-for="row in snapshot.poolResources" :key="`${row.pool}:${row.resource}`">
          <TableCell class="font-mono">{{ row.pool }}</TableCell>
          <TableCell>{{ $t(RESOURCE_LABELS[row.resource ?? 0] ?? $t('ui.message10')) }}</TableCell>
          <TableCell class="font-mono">{{ monitoringQuota(row.available, row.resource) }} / {{ monitoringQuota(row.total, row.resource) }}</TableCell>
          <TableCell class="font-mono">{{ monitoringQuotaPercent(row.available, row.total) === null ? $t('ui.noSamples') : `${monitoringNumber(monitoringQuotaPercent(row.available, row.total))}%` }}</TableCell>
          <TableCell class="font-mono">{{ monitoringNumber(row.onlineRunners, 0) }}</TableCell>
        </TableRow></TableBody>
      </Table>
      <p v-if="!snapshot.poolResources?.length" class="text-sm text-muted-foreground">{{ $t('ui.poolQuotaDetailsHaveNoSamplesOrAreUnavailableCheck') }}</p>
      <p class="text-sm text-muted-foreground">{{ $t('ui.useTheExistingGrafanaHostMonitoringForActualUtilizationSeparately') }}</p>
    </section>

    <div v-if="snapshot" class="flex flex-wrap items-center justify-between gap-2 border-t pt-4 text-xs text-muted-foreground">
      <span>{{ $t('ui.dataRefreshesAutomaticallyEvery15Seconds') }}</span>
      <span class="font-mono tabular-nums">{{ $t('ui.collectedAt') }}: {{ formatCapturedAt(snapshot.capturedAt) }}</span>
    </div>
  </div>
</template>
