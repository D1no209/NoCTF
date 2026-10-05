<script setup lang="ts">
import { toRefs } from 'vue'
import type { PrivateAccountPanelViewState } from '~/features/account/usePrivateAccountPanel'

const viewProps = defineProps<{ state: PrivateAccountPanelViewState }>()
const { LockKeyhole, data, loading, error, commonSources, kinds, load, AdminDateTime, competitionId, showActivities } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-4">
    <Skeleton v-if="loading" class="h-24 w-full" />
    <Alert v-else-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}<Button variant="outline" size="sm" @click="load">{{ $t('common.label.retry') }}</Button></AlertDescription></Alert>
    <template v-else-if="data">
      <section class="flex min-w-0 flex-col gap-2">
        <h3 class="flex items-center gap-2 font-semibold"><LockKeyhole class="size-4" />{{ $t('sso.externalIdentity') }}</h3>
        <div v-if="data.ssoBinding" class="flex items-center gap-3 rounded-xl bg-muted/45 p-3">
          <Avatar class="size-9">
            <AvatarImage v-if="data.ssoBinding.providerIconUrl" :src="data.ssoBinding.providerIconUrl" :alt="data.ssoBinding.providerName ?? ''" />
            <AvatarFallback>{{ data.ssoBinding.providerName?.slice(0, 1) ?? 'S' }}</AvatarFallback>
          </Avatar>
          <dl class="grid min-w-0 flex-1 grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
            <dt class="text-muted-foreground">{{ $t('sso.identityProvider') }}</dt><dd class="break-all">{{ data.ssoBinding.providerName || data.ssoBinding.providerId }}</dd>
            <dt class="text-muted-foreground">{{ $t('sso.protocol') }}</dt><dd>{{ data.ssoBinding.protocol }}</dd>
            <dt class="text-muted-foreground">{{ $t('sso.subject') }}</dt><dd class="break-all font-mono text-xs">{{ data.ssoBinding.subject }}</dd>
            <dt class="text-muted-foreground">{{ $t('sso.boundAt') }}</dt><dd><component :is="AdminDateTime" :value="data.ssoBinding.boundAt" /></dd>
          </dl>
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('sso.notBound') }}</p>
      </section>
      <Separator />
      <section v-if="!showActivities" class="flex min-w-0 flex-col gap-2" aria-labelledby="account-common-ips">
        <h3 id="account-common-ips" class="font-semibold">{{ $t('account.label.commonIpAddresses') }}</h3>
        <p class="text-xs text-muted-foreground">{{ $t('account.privateAccount.description.topKnownIpsRecent', { days: data.retentionDays ?? 30 }) }}</p>
        <p v-if="!commonSources.length" class="text-sm text-muted-foreground">{{ $t('account.privateAccount.label.knownSourceIpAddresses') }}</p>
        <ul v-else class="flex min-w-0 flex-col gap-2">
          <li v-for="source in commonSources" :key="source.address" class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 text-sm">
            <span class="min-w-0 break-all font-mono tabular-nums">{{ source.address }}</span>
            <span class="text-xs text-muted-foreground">{{ $t('account.label.seenTimes', { count: source.count }) }}<span v-if="source.lastSeen"> · {{ $t('account.label.lastSeen') }} <component :is="AdminDateTime" :value="source.lastSeen" /></span></span>
          </li>
        </ul>
      </section>
      <section class="flex min-w-0 flex-col gap-2">
        <h3 class="flex items-center gap-2 font-semibold"><LockKeyhole class="size-4" />{{ $t('account.label.personalInformation') }}</h3>
        <p class="text-xs text-muted-foreground">{{ $t('account.privateAccount.description.selfReportedUnverifiedInformation') }}</p>
        <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
          <dt class="text-muted-foreground">{{ $t('account.label.fullName') }}</dt><dd class="break-all">{{ data.identity?.fullName || $t('account.label.provided') }}</dd>
          <dt class="text-muted-foreground">{{ $t('account.label.studentNumber') }}</dt><dd class="break-all font-mono">{{ data.identity?.studentNumber || $t('account.label.provided') }}</dd>
        </dl>
      </section>
      <template v-if="showActivities">
        <Separator />
        <h4 class="font-medium">{{ $t('account.label.sourceIpActivity') }}</h4>
        <p class="text-xs text-muted-foreground">{{ $t('account.privateAccount.description.showsLatestEventsLast', { days: data.retentionDays ?? 30 }) }}</p>
        <p v-if="competitionId" class="text-xs text-muted-foreground">{{ $t('account.privateAccount.description.submissionsCompetitionIncludedNever') }}</p>
        <Empty v-if="!data.activities?.length"><EmptyDescription>{{ $t('account.label.retainedActivity') }}</EmptyDescription></Empty>
        <ol v-else class="flex flex-col divide-y">
          <li v-for="item in data.activities" :key="item.id" class="flex min-w-0 flex-col gap-1 py-3 text-sm">
            <div class="flex flex-wrap justify-between gap-2"><span>{{ translate(kinds[item.kind ?? ''] ?? item.kind ?? '') }}</span><time class="font-mono text-xs text-muted-foreground">{{ formatDateTime(item.occurredAt) }}</time></div>
            <span class="break-all font-mono">{{ item.ipAddress || $t('account.label.unknownSource') }}</span>
            <span v-if="item.gameplayFactId" class="break-all font-mono text-xs text-muted-foreground">{{ $t('common.label.submissionId') }}: {{ item.gameplayFactId }}</span>
            <span v-if="!competitionId && item.competitionId" class="break-all font-mono text-xs text-muted-foreground">{{ $t('account.label.competitionId') }}: {{ item.competitionId }}</span>
          </li>
        </ol>
      </template>
    </template>
  </section>
</template>
