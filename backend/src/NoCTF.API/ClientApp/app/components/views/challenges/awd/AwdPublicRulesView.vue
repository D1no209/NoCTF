<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdPublicRulesViewState } from '~/features/challenges/awd/useAwdPublicRules'
const props = defineProps<{ state: AwdPublicRulesViewState }>()
const { rules, loading, error, open, tab, selectedRulesId, policy, groups, flagWindow, historyWindow, health, serviceText, settlementText, observedAt, healthText, formatDate, loadRules } = toRefs(props.state)
</script>

<template>
  <section class="flex flex-col gap-3 py-5" aria-labelledby="awd-public-rules-title">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h3 id="awd-public-rules-title" class="text-sm font-semibold">{{ $t('challenges.awdRules.title') }}</h3>
      <Dialog v-model:open="open">
        <DialogTrigger as-child><Button size="sm" variant="outline" :disabled="!rules">{{ $t('challenges.awdRules.view') }}</Button></DialogTrigger>
        <DialogScrollContent class="sm:max-w-3xl">
          <DialogHeader><DialogTitle>{{ $t('challenges.awdRules.title') }}</DialogTitle><DialogDescription>{{ $t('challenges.awdRules.policyNotice') }}</DialogDescription></DialogHeader>
          <template v-if="rules">
            <FieldDescription v-if="error" class="text-destructive">{{ $t('challenges.awdRules.stale') }}</FieldDescription>
            <Tabs v-model="tab"><TabsList><TabsTrigger value="current" :disabled="!rules.issuedPolicies?.length">{{ $t('challenges.awdRules.current') }}</TabsTrigger><TabsTrigger value="next">{{ $t('challenges.awdRules.next') }}</TabsTrigger></TabsList></Tabs>
            <Field v-if="tab === 'current' && (rules.issuedPolicies?.length ?? 0) > 1"><FieldLabel>{{ $t('challenges.awdRules.revision') }}</FieldLabel><Select v-model="selectedRulesId"><SelectTrigger :aria-label="$t('challenges.awdRules.revision')"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="item in rules.issuedPolicies" :key="item.rulesId!" :value="item.rulesId!">{{ $t('challenges.awdRules.revisionNumber', { revision: item.revision ?? 0 }) }} · {{ formatDate(item.effectiveAt) }}</SelectItem></SelectContent></Select></Field>
            <FieldDescription v-if="tab === 'next'">{{ $t('challenges.awdRules.nextNotice') }}</FieldDescription>
            <div v-else class="flex flex-wrap gap-3 text-sm"><Badge variant="secondary">{{ $t('challenges.awdRules.round', { number: rules.roundNumber ?? 0 }) }}</Badge><span>{{ formatDate(rules.roundStartsAt) }} – {{ formatDate(rules.roundEndsAt) }}</span></div>
            <FieldDescription v-if="policy?.scoringChangeEventId">{{ $t('challenges.awdRules.amendmentNotice') }}</FieldDescription>
            <dl class="grid grid-cols-2 gap-4 text-sm"><div><dt class="text-muted-foreground">{{ $t('challenges.awdRules.flagWindow') }}</dt><dd class="mt-1 font-medium">{{ flagWindow }}</dd></div><div><dt class="text-muted-foreground">{{ $t('challenges.awdRules.historyWindow') }}</dt><dd class="mt-1 font-medium">{{ historyWindow }}</dd></div></dl>
            <FieldDescription>{{ $t('challenges.awdRules.windowNotice') }}</FieldDescription>
            <DefinitionSection v-for="group in groups" :key="`${tab}-${selectedRulesId}-${group.key}`" :title="group.label" :default-open="group.key === 'scoring'">
              <Table class="table-fixed"><TableHeader><TableRow><TableHead class="w-2/5">{{ $t('challenges.awdRules.field') }}</TableHead><TableHead>{{ $t('challenges.awdRules.value') }}</TableHead><TableHead>{{ $t('challenges.awdRules.source') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="row in group.rows" :key="row.key"><TableCell class="whitespace-normal break-words">{{ row.label }}</TableCell><TableCell class="whitespace-normal break-words font-mono tabular-nums">{{ row.value }}<small v-if="row.issued !== null" class="mt-1 block text-muted-foreground">{{ $t('challenges.awdRules.original', { value: row.issued }) }}</small></TableCell><TableCell class="whitespace-normal break-words text-xs text-muted-foreground">{{ row.source }}</TableCell></TableRow></TableBody></Table>
            </DefinitionSection>
            <DefinitionSection :title="$t('challenges.awdRules.stores')">
              <Table><TableHeader><TableRow><TableHead>{{ $t('challenges.awdRules.storeName') }}</TableHead><TableHead>{{ $t('challenges.awdRules.storeWeight') }}</TableHead><TableHead>{{ $t('challenges.awdRules.flagsPerIssue') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="store in policy?.stores" :key="store.storeId"><TableCell>{{ store.name }}</TableCell><TableCell class="font-mono tabular-nums">{{ store.weight }}</TableCell><TableCell class="font-mono tabular-nums">{{ store.flagsPerIssue }}</TableCell></TableRow></TableBody></Table>
              <FieldDescription>{{ $t('challenges.awdRules.storeNotice') }}</FieldDescription>
            </DefinitionSection>
            <FieldDescription>{{ $t('challenges.awdRules.scoreNotice') }}</FieldDescription>
          </template>
          <DialogFooter><DialogClose as-child><Button variant="outline">{{ $t('common.action.close') }}</Button></DialogClose></DialogFooter>
        </DialogScrollContent>
      </Dialog>
    </div>
    <Skeleton v-if="loading && !rules" class="h-20 w-full" />
    <div v-if="error" class="flex flex-wrap items-center gap-3"><FieldDescription class="text-destructive">{{ $message(error) }}</FieldDescription><Button variant="outline" size="sm" :disabled="loading" @click="loadRules">{{ $t('challenges.awdRules.refresh') }}</Button></div>
    <template v-if="rules">
      <div class="flex flex-wrap items-center gap-2 text-sm"><Badge v-if="rules.roundNumber" variant="secondary">{{ $t('challenges.awdRules.round', { number: rules.roundNumber }) }}</Badge><span>{{ $t(rules.paused ? 'challenges.awdRules.paused' : rules.acceptingSubmissions ? 'challenges.awdRules.accepting' : 'challenges.awdRules.closed') }}</span><span v-if="error" class="text-muted-foreground">{{ $t('challenges.awdRules.stale') }}</span></div>
      <FieldDescription v-if="rules.paused">{{ $t('challenges.awdRules.pauseNotice') }}</FieldDescription>
      <dl class="grid gap-3 text-sm sm:grid-cols-2"><div><dt class="text-muted-foreground">{{ $t('challenges.awdRules.endsAt') }}</dt><dd class="mt-1 font-mono tabular-nums">{{ formatDate(rules.endsAt) }}</dd></div><div><dt class="text-muted-foreground">{{ $t('challenges.awdRules.acceptanceEndsAt') }}</dt><dd class="mt-1 font-mono tabular-nums">{{ formatDate(rules.acceptanceEndsAt) }}</dd></div></dl>
      <div v-if="health" class="flex flex-col gap-2">
        <div class="flex flex-wrap items-center gap-2 text-sm"><span class="text-muted-foreground">{{ $t('challenges.awdRules.ownHealth') }}</span><Badge variant="outline">{{ serviceText }}</Badge><Badge variant="secondary">{{ settlementText }}</Badge><span v-if="health.observedAt" class="text-xs text-muted-foreground">{{ observedAt }}</span></div>
        <FieldDescription v-if="!health.observedAt && health.lastTrustedAt">{{ $t('challenges.awdRules.previousHealth', { health: healthText(health.lastTrustedHealth), time: formatDate(health.lastTrustedAt) }) }}</FieldDescription>
        <FieldDescription v-if="health.settlement === 'PendingAdjudication'">{{ $t('challenges.awdRules.pendingNotice') }}</FieldDescription>
        <FieldDescription>{{ $t('challenges.awdRules.healthNotice') }}</FieldDescription>
      </div>
    </template>
  </section>
</template>
