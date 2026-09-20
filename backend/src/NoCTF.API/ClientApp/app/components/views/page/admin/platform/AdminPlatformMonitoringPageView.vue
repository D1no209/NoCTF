<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformMonitoringPageViewState } from '~/features/routes/admin/platform/useAdminPlatformMonitoringPage'

const viewProps = defineProps<{ state: AdminPlatformMonitoringPageViewState }>()
const {
  runnerFailureLabel, runnerStateLabel, formatCapacityAmount, formatRunnerUsage,
  Activity,
  ExternalLinkIcon,
  RefreshCw,
  GROUPS,
  GAMEPLAY,
  HERO_KINDS,
  snapshot,
  loading,
  error,
  statusLabel,
  statusVariant,
  statusKey,
  groupStatus,
  metricByKind,
  metricLabel,
  formatMetric,
  metricVisualPercent,
  latencyVisualPercent,
  poolVisualPercent,
  poolStatusKey,
  healthPercent,
  healthyMetricCount,
  metricCount,
  formatCapturedAt,
  verificationProviderLabel,
  verificationStateLabel,
  verificationStatusKey,
  refreshMonitoring,
  LATENCY_LABELS,
  RESOURCE_LABELS,
} = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4 pb-2">
    <div class="flex flex-wrap items-center justify-between gap-4 pb-1">
      <div class="flex min-w-0 items-center gap-3">
        <div class="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary shadow-[0_0_24px_color-mix(in_oklch,var(--primary)_16%,transparent)]">
          <component :is="Activity" class="size-5" />
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="text-display text-xl">{{ $t('ui.monitoring') }}</h2>
          <Badge :variant="statusVariant(snapshot?.status)">
            {{ statusLabel(snapshot?.status) }}
          </Badge>
        </div>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button v-if="snapshot?.dashboardUrl" variant="outline" as-child>
          <ExternalLink :href="snapshot.dashboardUrl">
            <component :is="ExternalLinkIcon" data-icon="inline-start" />
            {{ $t('ui.openGrafana') }}
          </ExternalLink>
        </Button>
        <Button variant="outline" :disabled="loading" @click="refreshMonitoring">
          <Spinner v-if="loading" data-icon="inline-start" />
          <component :is="RefreshCw" v-else data-icon="inline-start" />
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

    <Card v-if="!snapshot && loading" class="overflow-hidden">
      <CardContent class="grid gap-4 p-5 lg:grid-cols-[15rem_minmax(0,1fr)]">
        <Skeleton class="h-36 w-full" />
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <Skeleton v-for="index in 6" :key="index" class="h-24 w-full" />
        </div>
      </CardContent>
    </Card>

    <template v-else-if="snapshot">
      <Card class="overflow-hidden">
        <CardContent class="p-0">
          <div class="grid gap-6 p-5 lg:grid-cols-[16rem_minmax(0,1fr)] lg:p-6">
            <div class="flex items-center gap-5 lg:flex-col lg:items-start lg:justify-between">
              <div class="flex items-center gap-4">
                <div
                  data-slot="monitoring-health-orbit"
                  :data-monitoring-status="statusKey(snapshot.status)"
                  :style="{ '--monitoring-health-value': `${healthPercent}%` }"
                >
                  <span class="font-mono text-lg font-bold tabular-nums">{{ healthyMetricCount }}/{{ metricCount }}</span>
                </div>
                <div>
                  <p class="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">{{ $t('ui.status') }}</p>
                  <p class="mt-1 text-display text-lg">{{ statusLabel(snapshot.status) }}</p>
                  <p class="mt-1 text-xs text-muted-foreground">{{ $t('ui.healthy') }}</p>
                </div>
              </div>
              <div class="grid min-w-0 flex-1 gap-2 lg:w-full lg:flex-none">
                <div class="flex items-center justify-between gap-3 rounded-xl bg-muted/35 px-3 py-2 text-sm">
                  <span class="text-muted-foreground">{{ $t('ui.prometheus') }}</span>
                  <span class="flex items-center gap-2 font-medium">
                    <span data-slot="monitoring-status-dot" :data-monitoring-status="snapshot.prometheusAvailable ? 'healthy' : 'critical'" />
                    {{ $t(snapshot.prometheusAvailable ? 'ui.available' : 'ui.unavailable2') }}
                  </span>
                </div>
                <div class="flex items-center justify-between gap-3 rounded-xl bg-muted/35 px-3 py-2 text-sm">
                  <span class="text-muted-foreground">{{ $t('ui.nats') }}</span>
                  <span class="flex items-center gap-2 font-medium">
                    <span data-slot="monitoring-status-dot" :data-monitoring-status="snapshot.natsAvailable ? 'healthy' : 'critical'" />
                    {{ $t(snapshot.natsAvailable ? 'ui.available' : 'ui.unavailable2') }}
                  </span>
                </div>
                <div
                  v-if="snapshot.humanVerification"
                  class="grid gap-1 rounded-xl bg-muted/35 px-3 py-2 text-sm"
                  :data-monitoring-status="verificationStatusKey(snapshot.humanVerification.state, snapshot.humanVerification.enabled)"
                >
                  <div class="flex items-center justify-between gap-3">
                    <span class="text-muted-foreground">{{ $t('ui.humanVerification') }} · {{ verificationProviderLabel(snapshot.humanVerification.provider) }}</span>
                    <span class="flex items-center gap-2 font-medium">
                      <span data-slot="monitoring-status-dot" :data-monitoring-status="verificationStatusKey(snapshot.humanVerification.state, snapshot.humanVerification.enabled)" />
                      {{ verificationStateLabel(snapshot.humanVerification.state) }}
                    </span>
                  </div>
                  <div class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-[0.68rem] text-muted-foreground">
                    <span>{{ $t(snapshot.humanVerification.enabled ? 'ui.enabled2' : 'ui.disabled') }}</span>
                    <span v-if="snapshot.humanVerification.checkedAt" class="font-mono tabular-nums">
                      {{ formatCapturedAt(snapshot.humanVerification.checkedAt) }}
                      <template v-if="snapshot.humanVerification.latencyMilliseconds != null">
                        · {{ snapshot.humanVerification.latencyMilliseconds }} {{ $t('ui.millisecondsShort') }}
                      </template>
                    </span>
                  </div>
                </div>
              </div>
            </div>

            <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              <div
                v-for="kind in HERO_KINDS"
                :key="kind"
                class="flex min-h-24 flex-col justify-between rounded-xl bg-muted/30 p-3.5"
                :data-monitoring-status="statusKey(metricByKind(kind)?.status)"
              >
                <div class="flex items-start justify-between gap-2">
                  <p class="text-xs leading-snug text-muted-foreground">{{ $t(metricLabel(kind)) }}</p>
                  <span data-slot="monitoring-status-dot" :data-monitoring-status="statusKey(metricByKind(kind)?.status)" />
                </div>
                <p class="my-2 font-mono text-2xl font-semibold tabular-nums">{{ formatMetric(metricByKind(kind)) }}</p>
                <div
                  data-slot="monitoring-meter"
                  role="progressbar"
                  :aria-label="$t(metricLabel(kind))"
                  :aria-valuenow="metricVisualPercent(metricByKind(kind))"
                  aria-valuemin="0"
                  aria-valuemax="100"
                >
                  <span :style="{ width: `${metricVisualPercent(metricByKind(kind))}%` }" />
                </div>
              </div>
            </div>
          </div>

        </CardContent>
      </Card>

      <Card data-slot="gameplay-monitoring" class="overflow-hidden">
        <CardHeader class="pb-3">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="max-w-3xl">
              <CardTitle class="text-base">{{ $t(GAMEPLAY.title) }}</CardTitle>
              <CardDescription class="mt-1">{{ $t('ui.gameplaySubmissionHealthDescription') }}</CardDescription>
            </div>
            <Badge :variant="statusVariant(groupStatus(GAMEPLAY))">{{ statusLabel(groupStatus(GAMEPLAY)) }}</Badge>
          </div>
        </CardHeader>
        <CardContent class="p-0">
          <div class="grid lg:grid-cols-[minmax(0,1.35fr)_minmax(18rem,1fr)]">
            <section class="flex min-h-44 flex-col justify-between bg-primary/[0.035] p-5 sm:p-6">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <p class="text-sm font-semibold">{{ $t(metricLabel(GAMEPLAY.qualityKind)) }}</p>
                  <p v-if="metricByKind(GAMEPLAY.qualityKind)?.minimumSamples" class="mt-1 text-xs text-muted-foreground">
                    {{ $t('ui.samplesInTheLastFiveMinutes') }} {{ monitoringNumber(metricByKind(GAMEPLAY.qualityKind)?.sampleCount, 0) }}
                  </p>
                </div>
                <span data-slot="monitoring-status-dot" :data-monitoring-status="statusKey(metricByKind(GAMEPLAY.qualityKind)?.status)" />
              </div>
              <p class="my-5 font-mono text-3xl font-semibold tabular-nums">{{ formatMetric(metricByKind(GAMEPLAY.qualityKind)) }}</p>
              <div
                data-slot="monitoring-meter"
                role="progressbar"
                :aria-label="$t(metricLabel(GAMEPLAY.qualityKind))"
                :aria-valuenow="metricVisualPercent(metricByKind(GAMEPLAY.qualityKind))"
                aria-valuemin="0"
                aria-valuemax="100"
              >
                <span :style="{ width: `${metricVisualPercent(metricByKind(GAMEPLAY.qualityKind))}%` }" />
              </div>
            </section>

            <div class="grid divide-y divide-border/40">
              <section
                v-for="kind in GAMEPLAY.healthKinds"
                :key="kind"
                class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-3 p-5"
                :data-monitoring-status="statusKey(metricByKind(kind)?.status)"
              >
                <div class="min-w-0">
                  <p class="text-sm font-semibold">{{ $t(metricLabel(kind)) }}</p>
                  <p v-if="metricByKind(kind)?.minimumSamples" class="mt-1 text-xs text-muted-foreground">
                    {{ monitoringNumber(metricByKind(kind)?.sampleCount, 0) }} / {{ $t('ui.minimumSamples') }} {{ metricByKind(kind)?.minimumSamples }}
                  </p>
                </div>
                <div class="flex items-center gap-2">
                  <span data-slot="monitoring-status-dot" :data-monitoring-status="statusKey(metricByKind(kind)?.status)" />
                  <span class="font-mono text-xl font-semibold tabular-nums">{{ formatMetric(metricByKind(kind)) }}</span>
                </div>
                <div
                  class="col-span-2"
                  data-slot="monitoring-meter"
                  role="progressbar"
                  :aria-label="$t(metricLabel(kind))"
                  :aria-valuenow="metricVisualPercent(metricByKind(kind))"
                  aria-valuemin="0"
                  aria-valuemax="100"
                >
                  <span :style="{ width: `${metricVisualPercent(metricByKind(kind))}%` }" />
                </div>
              </section>
            </div>
          </div>

          <Separator />

          <div class="grid bg-muted/20 sm:grid-cols-2 xl:grid-cols-4">
            <div v-for="kind in GAMEPLAY.volumeKinds" :key="kind" class="flex items-center justify-between gap-4 px-5 py-4">
              <span class="text-xs text-muted-foreground">{{ $t(metricLabel(kind)) }}</span>
              <span class="whitespace-nowrap font-mono text-sm font-semibold tabular-nums">{{ formatMetric(metricByKind(kind)) }}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="overflow-hidden">
        <CardContent class="p-0">
          <div class="grid gap-px bg-muted/30 lg:grid-cols-2">
            <section
              v-for="group in GROUPS"
              :key="group.title"
              class="bg-card/70 p-5"
              :data-monitoring-status="statusKey(groupStatus(group))"
            >
              <div class="mb-2 flex items-center justify-between gap-3">
                <h3 class="text-sm font-semibold">{{ $t(group.title) }}</h3>
                <Badge :variant="statusVariant(groupStatus(group))">{{ statusLabel(groupStatus(group)) }}</Badge>
              </div>
              <div class="divide-y divide-border/40">
                <div v-for="kind in group.kinds" :key="kind" class="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-2 py-3">
                  <div class="min-w-0">
                    <p class="truncate text-xs text-muted-foreground">{{ $t(metricLabel(kind)) }}</p>
                    <p v-if="metricByKind(kind)?.minimumSamples" class="mt-0.5 text-[0.68rem] text-muted-foreground/75">
                      {{ monitoringNumber(metricByKind(kind)?.sampleCount, 0) }} / {{ $t('ui.minimumSamples') }} {{ metricByKind(kind)?.minimumSamples }}
                    </p>
                  </div>
                  <div class="flex items-center gap-2 self-start">
                    <span data-slot="monitoring-status-dot" :data-monitoring-status="statusKey(metricByKind(kind)?.status)" />
                    <span class="font-mono text-sm font-semibold tabular-nums">{{ formatMetric(metricByKind(kind)) }}</span>
                  </div>
                  <div
                    class="col-span-2"
                    data-slot="monitoring-meter"
                    role="progressbar"
                    :aria-label="$t(metricLabel(kind))"
                    :aria-valuenow="metricVisualPercent(metricByKind(kind))"
                    aria-valuemin="0"
                    aria-valuemax="100"
                  >
                    <span :style="{ width: `${metricVisualPercent(metricByKind(kind))}%` }" />
                  </div>
                </div>
              </div>
            </section>
          </div>
        </CardContent>
      </Card>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(20rem,0.85fr)]">
        <Card class="overflow-hidden">
          <CardHeader class="pb-3">
            <div class="flex items-center justify-between gap-3">
              <CardTitle class="text-base">{{ $t('ui.requestAndOperationDurationDetails') }}</CardTitle>
              <Badge variant="outline" class="font-mono tabular-nums">{{ snapshot.latencyDetails?.length ?? 0 }}</Badge>
            </div>
          </CardHeader>
          <CardContent class="grid gap-2 pb-5">
            <div
              v-for="row in snapshot.latencyDetails"
              :key="`${row.kind}:${row.endpoint}`"
              class="rounded-xl bg-muted/30 p-3.5"
              :data-monitoring-status="statusKey(row.status)"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="text-sm font-semibold">{{ $t(LATENCY_LABELS[row.kind ?? 0] ?? 'ui.message10') }}</p>
                  <p v-if="row.endpoint" class="mt-0.5 truncate font-mono text-xs text-muted-foreground">{{ row.endpoint }}</p>
                </div>
                <Badge :variant="statusVariant(row.status)">{{ statusLabel(row.status) }}</Badge>
              </div>
              <div class="mt-3" data-slot="monitoring-latency-meter">
                <span data-range="p99" :style="{ width: `${latencyVisualPercent(row.p99Milliseconds)}%` }" />
                <span data-range="p95" :style="{ width: `${latencyVisualPercent(row.p95Milliseconds)}%` }" />
              </div>
              <div class="mt-3 grid grid-cols-3 gap-x-3 gap-y-2 sm:grid-cols-6">
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.p952') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringMilliseconds(row.p95Milliseconds) }}</p></div>
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.p99') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringMilliseconds(row.p99Milliseconds) }}</p></div>
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.mean') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringMilliseconds(row.meanMilliseconds) }}</p></div>
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.rate') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringNumber(row.requestsPerSecond, 2, ' /s') }}</p></div>
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.errorRate') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringNumber(row.errorPercent, 2, '%') }}</p></div>
                <div><p class="text-[0.65rem] uppercase text-muted-foreground">{{ $t('ui.samplesInTheLastFiveMinutes') }}</p><p class="font-mono text-xs font-semibold tabular-nums">{{ monitoringNumber(row.sampleCount, 0) }}</p></div>
              </div>
            </div>
            <p v-if="!snapshot.latencyDetails?.some(row => row.kind === 4)" class="py-3 text-sm text-muted-foreground">
              {{ $t('ui.redisOperationDetailsHaveNoSamplesOrAreUnavailableCheck') }}
            </p>
            <p class="pt-2 text-xs leading-relaxed text-muted-foreground">
              {{ $t('ui.quantilesMeansSamplesRatesAndErrorsShareTheSameFive') }}
              {{ $t('ui.latencyAlertsRequireMinimumSamplesAndASustainedThresholdBreach') }}：{{ snapshot.latencySustainedWindowMinutes }}。
            </p>
          </CardContent>
        </Card>

        <Card class="overflow-hidden">
          <CardHeader class="pb-3">
            <div class="flex items-center justify-between gap-3">
              <CardTitle class="text-base">{{ $t('ui.remainingResourcePoolQuotas') }}</CardTitle>
              <Badge variant="outline" class="font-mono tabular-nums">{{ snapshot.poolResources?.length ?? 0 }}</Badge>
            </div>
          </CardHeader>
          <CardContent class="grid gap-2 pb-5">
            <div
              v-for="row in snapshot.poolResources"
              :key="`${row.pool}:${row.resource}`"
              class="rounded-xl bg-muted/30 p-3.5"
              :data-monitoring-status="poolStatusKey(row.available, row.total)"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="truncate font-mono text-sm font-semibold">{{ row.pool }}</p>
                  <p class="mt-0.5 text-xs text-muted-foreground">{{ $t(RESOURCE_LABELS[row.resource ?? 0] ?? 'ui.message10') }}</p>
                </div>
                <p class="font-mono text-lg font-semibold tabular-nums">
                  {{ monitoringQuotaPercent(row.available, row.total) === null ? $t('ui.noSamples') : `${monitoringNumber(monitoringQuotaPercent(row.available, row.total))}%` }}
                </p>
              </div>
              <div
                class="mt-3"
                data-slot="monitoring-meter"
                role="progressbar"
                :aria-label="`${row.pool} ${$t(RESOURCE_LABELS[row.resource ?? 0] ?? 'ui.message10')}`"
                :aria-valuenow="poolVisualPercent(row.available, row.total)"
                aria-valuemin="0"
                aria-valuemax="100"
              >
                <span :style="{ width: `${poolVisualPercent(row.available, row.total)}%` }" />
              </div>
              <div class="mt-2 flex items-center justify-between gap-3 font-mono text-[0.68rem] text-muted-foreground">
                <span>{{ monitoringQuota(row.available, row.resource) }} / {{ monitoringQuota(row.total, row.resource) }}</span>
                <span>{{ $t('ui.onlineRunners') }} {{ monitoringNumber(row.onlineRunners, 0) }}</span>
              </div>
            </div>
            <p v-if="!snapshot.poolResources?.length" class="py-3 text-sm text-muted-foreground">{{ $t('ui.poolQuotaDetailsHaveNoSamplesOrAreUnavailableCheck') }}</p>
            <p class="pt-2 text-xs leading-relaxed text-muted-foreground">{{ $t('ui.theMinimumIsTakenAcrossEachPoolSAggregatedAvailable') }}</p>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader><CardTitle>{{ $t('capacity.title') }}</CardTitle></CardHeader>
        <CardContent>
          <Empty v-if="!snapshot.capacity?.available">{{ $t('capacity.unavailable') }}</Empty>
          <template v-else>
            <FieldDescription>{{ $t('capacity.units') }}</FieldDescription>
            <Table>
              <TableHeader><TableRow>
                <TableHead>{{ $t('capacity.runner') }}</TableHead>
                <TableHead>{{ $t('capacity.state') }}</TableHead>
                <TableHead>{{ $t('capacity.observedTotal') }}</TableHead>
                <TableHead>{{ $t('capacity.observedAvailable') }}</TableHead>
                <TableHead>{{ $t('capacity.safetyHeadroom') }}</TableHead>
                <TableHead>{{ $t('capacity.startupReserved') }}</TableHead>
                <TableHead>{{ $t('capacity.admissionAvailable') }}</TableHead>
                <TableHead>{{ $t('capacity.declaredLimits') }}</TableHead>
                <TableHead>{{ $t('capacity.usage') }}</TableHead>
                <TableHead>{{ $t('capacity.observed') }}</TableHead>
              </TableRow></TableHeader>
              <TableBody><TableRow v-for="runner in snapshot.capacity.runners" :key="runner.runnerId">
                <TableCell><span class="font-mono">{{ runner.runnerId }}</span><Badge variant="secondary">{{ $t(runner.alive ? 'capacity.alive' : 'capacity.offline') }}</Badge></TableCell>
                <TableCell>{{ runnerStateLabel(runner.state) }}<FieldDescription v-if="runner.failure">{{ runnerFailureLabel(runner.failure) }}</FieldDescription><FieldDescription v-if="runner.startingPrimary != null || runner.startingAuxiliary != null">{{ $t('capacity.startingCounts', { primary: runner.startingPrimary ?? 0, auxiliary: runner.startingAuxiliary ?? 0 }) }}</FieldDescription></TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.observedTotal) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.observedAvailable) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.safetyHeadroom) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.startupReserved) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.admissionAvailable) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatCapacityAmount(runner.declaredLimits) }}</TableCell>
                <TableCell class="font-mono tabular-nums">{{ formatRunnerUsage(runner) }}</TableCell>
                <TableCell>{{ formatCapturedAt(runner.observation?.observedAt) }}</TableCell>
              </TableRow></TableBody>
            </Table>
            <FieldDescription v-if="snapshot.capacity.truncated">{{ $t('capacity.truncated') }}</FieldDescription>
          </template>
        </CardContent>
      </Card>

      <div class="flex flex-wrap items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>{{ $t('ui.dataRefreshesAutomaticallyEvery15Seconds') }}</span>
        <span class="font-mono tabular-nums">{{ $t('ui.collectedAt') }}: {{ formatCapturedAt(snapshot.capturedAt) }}</span>
      </div>
    </template>
  </div>
</template>

<style src="./platform-monitoring.css"></style>
