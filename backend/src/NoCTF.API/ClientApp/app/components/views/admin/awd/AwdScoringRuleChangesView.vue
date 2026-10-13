<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdScoringRuleChangesViewState } from '~/features/admin/awd/useAwdScoringRuleChanges'
const props = defineProps<{ state: AwdScoringRuleChangesViewState }>()
const { requestedRound, rules, selectedRulesId, selectedPolicy, values, firstRound, lastRound, timing, reason, loading, readError, actionError,
  preview, previewOpen, previewLoading, saving, previewExpired, canPreview, canApply, groups, invalidFields, validRange, policyRows, proposedRows,
  changedTeams, updateField, loadRules, previewChanges, applyChanges, closePreview, auditOpen, auditRows, showAuditPolicy,
  impactItems, impactPage, impactPageCount, impactLimit, impactTotal, loadImpactPage, setImpactLimit, ConfigFieldInput, DefinitionSection } = toRefs(props.state)
</script>

<template>
  <Card>
    <CardHeader><CardTitle>{{ $t('administration.awd.scoring.title') }}</CardTitle></CardHeader>
    <CardContent class="flex flex-col gap-5">
      <FieldDescription>{{ $t('administration.awd.scoring.scopeNotice') }}</FieldDescription>
      <FieldGroup class="flex flex-row flex-wrap items-end gap-3">
        <Field class="max-w-xs"><FieldLabel>{{ $t('administration.awd.scoring.round') }}</FieldLabel><NullableNumberInput v-model="requestedRound" :min="1" :disabled="loading || saving" :aria-label="$t('administration.awd.scoring.round')" :placeholder="$t('administration.awd.scoring.latestPlaceholder')" /></Field>
        <Button variant="outline" :disabled="loading || saving" @click="loadRules"><Spinner v-if="loading" data-icon="inline-start" />{{ $t('administration.awd.scoring.load') }}</Button>
      </FieldGroup>
      <FieldDescription v-if="readError" class="text-destructive">{{ $message(readError) }}</FieldDescription>
      <Skeleton v-if="loading" class="h-32 w-full" />
      <template v-else-if="rules">
        <Empty v-if="!rules.policies?.length">{{ $t('administration.awd.scoring.noRules') }}</Empty>
        <template v-else>
          <div class="flex flex-wrap items-center gap-3"><Badge variant="outline">{{ $t('administration.awd.review.round') }} {{ rules.roundNumber }}</Badge><span class="text-xs text-muted-foreground">{{ $t('administration.awd.scoring.issuedAt') }}: {{ adminFormatDateTime(selectedPolicy?.enabledAt) }}</span></div>
          <Field v-if="rules.policies.length > 1"><FieldLabel>{{ $t('administration.awd.scoring.snapshot') }}</FieldLabel><Select v-model="selectedRulesId" :disabled="saving"><SelectTrigger :aria-label="$t('administration.awd.scoring.snapshot')"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="policy in rules.policies" :key="policy.rulesId" :value="policy.rulesId!">{{ $t('administration.awd.scoring.revision', { revision: policy.revision ?? 0 }) }} · {{ adminFormatDateTime(policy.enabledAt) }}</SelectItem></SelectContent></Select></Field>
          <component :is="DefinitionSection" :title="$t('administration.awd.scoring.comparison')">
            <Table class="table-fixed"><TableHeader><TableRow><TableHead>{{ $t('administration.awd.scoring.field') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.issued') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.effective') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="row in policyRows" :key="row.key"><TableCell class="whitespace-normal break-words">{{ row.label }}</TableCell><TableCell class="whitespace-normal break-words">{{ row.issued }}</TableCell><TableCell :class="row.changed ? 'whitespace-normal break-words font-medium text-primary' : 'whitespace-normal break-words'">{{ row.effective }}</TableCell></TableRow></TableBody></Table>
          </component>
          <Separator />
          <h3 class="text-base font-medium">{{ $t('administration.awd.scoring.candidate') }}</h3>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field class="sm:col-span-2"><FieldLabel>{{ $t('administration.awd.scoring.timing') }}</FieldLabel><Select v-model="timing" :disabled="saving"><SelectTrigger :aria-label="$t('administration.awd.scoring.timing')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="RecalculateHistory">{{ $t('administration.awd.scoring.history') }}</SelectItem><SelectItem value="Immediately">{{ $t('administration.awd.scoring.immediately') }}</SelectItem></SelectContent></Select><FieldDescription>{{ $t(timing === 'Immediately' ? 'administration.awd.scoring.immediatelyHint' : 'administration.awd.scoring.historyHint') }}</FieldDescription></Field>
            <Field><FieldLabel>{{ $t('administration.awd.scoring.firstRound') }}</FieldLabel><NullableNumberInput v-model="firstRound" :min="1" :disabled="saving" :aria-label="$t('administration.awd.scoring.firstRound')" /></Field>
            <Field><FieldLabel>{{ $t('administration.awd.scoring.lastRound') }}</FieldLabel><NullableNumberInput v-model="lastRound" :min="1" :disabled="saving" :aria-label="$t('administration.awd.scoring.lastRound')" /></Field>
          </FieldGroup>
          <FieldDescription v-if="!validRange" class="text-destructive">{{ $t('administration.awd.scoring.rangeInvalid') }}</FieldDescription>
          <component :is="DefinitionSection" v-for="group in groups" :key="group.key" :title="group.title" :collapsible="group.key !== 'scoring'" :default-open="group.fields.some(field => invalidFields.includes(field.key))">
            <FieldGroup class="grid gap-3 sm:grid-cols-2"><Field v-for="field in group.fields" :key="field.key" :data-invalid="invalidFields.includes(field.key) || undefined"><FieldLabel>{{ field.label }}</FieldLabel><component :is="ConfigFieldInput" :field="field" :model-value="values[field.key]" :disabled="saving" @update:model-value="updateField(field.key, $event)" /></Field></FieldGroup>
          </component>
          <FieldDescription v-if="invalidFields.length" class="text-destructive">{{ $t('administration.awd.invalidField') }}</FieldDescription>
          <Field><FieldLabel>{{ $t('administration.awd.scoring.reason') }}</FieldLabel><Textarea v-model="reason" :disabled="saving" :maxlength="512" :aria-label="$t('administration.awd.scoring.reason')" /></Field>
          <FieldDescription v-if="actionError" class="text-destructive">{{ $message(actionError) }}</FieldDescription>
          <Button class="w-fit" :disabled="!canPreview" @click="previewChanges"><Spinner v-if="previewLoading" data-icon="inline-start" />{{ $t('administration.awd.scoring.preview') }}</Button>
        </template>
        <Separator />
        <component :is="DefinitionSection" :title="$t('administration.awd.scoring.audit')" :hint="$t('administration.awd.scoring.auditHint')" :default-open="true">
          <Empty v-if="!rules.recentChanges?.length">{{ $t('administration.awd.scoring.emptyAudit') }}</Empty>
          <div v-for="change in rules.recentChanges" :key="change.eventId" class="flex flex-col gap-2 py-3 text-sm">
            <div class="flex flex-wrap items-center gap-2"><Badge variant="secondary">{{ $t(change.timing === 'Immediately' ? 'administration.awd.scoring.immediately' : 'administration.awd.scoring.history') }}</Badge><span class="font-mono tabular-nums">{{ $t('administration.awd.review.round') }} {{ change.firstRound }}–{{ change.lastRound }}</span><span>{{ adminFormatDateTime(change.appliedAt) }}</span></div>
            <p class="whitespace-pre-wrap break-words">{{ change.reason }}</p>
            <p class="break-all text-xs text-muted-foreground">{{ $t('administration.awd.scoring.actor') }}: {{ change.actorUserId ?? '—' }}</p>
            <p class="break-all text-xs text-muted-foreground">{{ $t('administration.awd.scoring.event') }}: {{ change.eventId }}</p>
            <Button variant="outline" size="sm" class="w-fit" @click="showAuditPolicy(change.eventId)">{{ $t('administration.awd.scoring.auditPolicy') }}</Button>
            <Separator />
          </div>
        </component>
      </template>
    </CardContent>
  </Card>
  <Dialog v-model:open="previewOpen">
    <DialogScrollContent class="sm:max-w-4xl">
      <DialogHeader><DialogTitle>{{ $t('administration.awd.scoring.previewTitle') }}</DialogTitle><DialogDescription>{{ $t('administration.awd.scoring.previewHint') }}</DialogDescription></DialogHeader>
      <template v-if="preview">
        <div class="flex flex-wrap gap-3 text-sm"><span>{{ $t('administration.awd.scoring.affectedFlags') }}: {{ preview.affectedFlags }}</span><span>{{ $t('administration.awd.scoring.affectedSnapshots') }}: {{ preview.affectedRuleSnapshots }}</span><span>{{ $t('administration.awd.scoring.changedTeams') }}: {{ changedTeams }}</span></div>
        <p class="text-xs text-muted-foreground">{{ $t('administration.awd.scoring.projectedAt') }}: {{ adminFormatDateTime(preview.projectedAt) }} · {{ $t('administration.awd.scoring.expiresAt') }}: {{ adminFormatDateTime(preview.expiresAt) }}</p>
        <p class="text-sm">{{ $t('administration.awd.review.round') }} {{ firstRound }}–{{ lastRound }} · {{ $t(timing === 'Immediately' ? 'administration.awd.scoring.immediately' : 'administration.awd.scoring.history') }}</p>
        <p class="whitespace-pre-wrap break-words text-sm">{{ reason }}</p>
        <component :is="DefinitionSection" :title="$t('administration.awd.scoring.proposedPolicy')" :hint="$t('administration.awd.scoring.uniformPolicy')">
          <Table class="table-fixed"><TableHeader><TableRow><TableHead>{{ $t('administration.awd.scoring.field') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.proposedPolicy') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="row in proposedRows" :key="row.key"><TableCell class="whitespace-normal break-words">{{ row.label }}</TableCell><TableCell class="whitespace-normal break-words">{{ row.effective }}</TableCell></TableRow></TableBody></Table>
        </component>
        <Empty v-if="!impactItems.length">{{ $t('administration.awd.scoring.noTeams') }}</Empty>
        <Table v-else><TableHeader><TableRow><TableHead>{{ $t('administration.awd.review.team') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.before') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.after') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.delta') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="team in impactItems" :key="team.teamId"><TableCell>{{ team.teamName || team.teamId }}</TableCell><TableCell class="font-mono tabular-nums">{{ team.scoreBefore }}</TableCell><TableCell class="font-mono tabular-nums">{{ team.scoreAfter }}</TableCell><TableCell class="font-mono tabular-nums">{{ (team.scoreAfter ?? 0) - (team.scoreBefore ?? 0) }}</TableCell></TableRow></TableBody></Table>
        <OffsetPagination :page="impactPage" :page-count="impactPageCount" :total="impactTotal" :limit="impactLimit" :loading="saving" @update:page="loadImpactPage" @update:limit="setImpactLimit" />
        <FieldDescription v-if="previewExpired" class="text-destructive">{{ $t('administration.awd.scoring.expired') }}</FieldDescription>
      </template>
      <DialogFooter><Button variant="ghost" :disabled="saving" @click="closePreview">{{ $t('common.action.cancel') }}</Button><Button :disabled="!canApply" @click="applyChanges"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('administration.awd.scoring.apply') }}</Button></DialogFooter>
    </DialogScrollContent>
  </Dialog>
  <Dialog v-model:open="auditOpen"><DialogScrollContent class="sm:max-w-2xl"><DialogHeader><DialogTitle>{{ $t('administration.awd.scoring.auditPolicy') }}</DialogTitle></DialogHeader><Table class="table-fixed"><TableHeader><TableRow><TableHead>{{ $t('administration.awd.scoring.field') }}</TableHead><TableHead>{{ $t('administration.awd.scoring.effective') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="row in auditRows" :key="row.key"><TableCell class="whitespace-normal break-words">{{ row.label }}</TableCell><TableCell class="whitespace-normal break-words">{{ row.effective }}</TableCell></TableRow></TableBody></Table></DialogScrollContent></Dialog>
</template>
