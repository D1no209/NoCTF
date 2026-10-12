<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdCheckReviewViewState } from '~/features/admin/awd/useAwdCheckReview'
const props = defineProps<{ state: AwdCheckReviewViewState }>()
const { team, challenge, round, roundValid, settlement, settlementOptions, teams, challenges, canJudge, teamName, challengeTitle, label,
  items, total, page, pageCount, pageLimit, loading, listError, loadPage, setPageSize, refresh,
  detailOpen, evidence, selected, detailLoading, detailError, action, reason, saving, needsReload, canAccept, canAdjudicate, loadEvidence, openEvidence, adjudicate, evidenceTimes } = toRefs(props.state)
</script>

<template>
  <Card>
    <CardHeader class="flex flex-row flex-wrap items-center justify-between gap-3">
      <CardTitle>{{ $t('administration.awd.review.title') }}</CardTitle>
      <Button variant="outline" size="sm" :disabled="loading || saving" @click="refresh"><Spinner v-if="loading" data-icon="inline-start" />{{ $t('common.label.refresh') }}</Button>
    </CardHeader>
    <CardContent class="flex flex-col gap-4">
      <FieldGroup class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Field><FieldLabel>{{ $t('administration.awd.review.settlement') }}</FieldLabel>
          <Select v-model="settlement" :disabled="saving"><SelectTrigger :aria-label="$t('administration.awd.review.settlement')"><SelectValue /></SelectTrigger><SelectContent>
            <SelectItem value="all">{{ $t('administration.awd.review.all') }}</SelectItem><SelectItem v-for="item in settlementOptions" :key="item" :value="item">{{ label(item) }}</SelectItem>
          </SelectContent></Select>
        </Field>
        <Field><FieldLabel>{{ $t('administration.awd.review.team') }}</FieldLabel>
          <Select v-model="team" :disabled="saving"><SelectTrigger :aria-label="$t('administration.awd.review.team')"><SelectValue /></SelectTrigger><SelectContent>
            <SelectItem value="all">{{ $t('administration.awd.review.all') }}</SelectItem><SelectItem v-for="item in teams" :key="item.id" :value="item.id">{{ item.name }}</SelectItem>
          </SelectContent></Select>
        </Field>
        <Field><FieldLabel>{{ $t('administration.awd.review.challenge') }}</FieldLabel>
          <Select v-model="challenge" :disabled="saving"><SelectTrigger :aria-label="$t('administration.awd.review.challenge')"><SelectValue /></SelectTrigger><SelectContent>
            <SelectItem value="all">{{ $t('administration.awd.review.all') }}</SelectItem><SelectItem v-for="item in challenges" :key="item.id" :value="item.id">{{ item.title }}</SelectItem>
          </SelectContent></Select>
        </Field>
        <Field><FieldLabel>{{ $t('administration.awd.review.round') }}</FieldLabel><NullableNumberInput v-model="round" :min="1" :step="1" :disabled="saving" :aria-label="$t('administration.awd.review.round')" /></Field>
      </FieldGroup>
      <FieldDescription v-if="!roundValid" class="text-destructive">{{ $t('administration.awd.review.roundInvalid') }}</FieldDescription>
      <FieldDescription v-if="listError" class="text-destructive">{{ $message(listError.displayMessage) }}</FieldDescription>
      <Skeleton v-if="loading && !items.length" class="h-32 w-full" />
      <Empty v-else-if="!items.length">{{ $t('administration.awd.review.empty') }}</Empty>
      <Table v-else>
        <TableHeader><TableRow><TableHead>{{ $t('administration.awd.review.round') }}</TableHead><TableHead>{{ $t('administration.awd.review.team') }}</TableHead><TableHead>{{ $t('administration.awd.review.challenge') }}</TableHead><TableHead>{{ $t('administration.awd.review.health') }}</TableHead><TableHead>{{ $t('administration.awd.review.settlement') }}</TableHead><TableHead>{{ $t('administration.awd.review.reason') }}</TableHead><TableHead /></TableRow></TableHeader>
        <TableBody><TableRow v-for="item in items" :key="item.gameplayFactId">
          <TableCell class="font-mono tabular-nums">{{ item.roundNumber }}<p class="text-xs text-muted-foreground">{{ adminFormatDateTime(item.scheduledAt) }}</p></TableCell>
          <TableCell>{{ teamName(item.teamId) }}</TableCell><TableCell>{{ challengeTitle(item.competitionChallengeId) }}</TableCell>
          <TableCell>{{ label(item.health) }}<p class="text-xs text-muted-foreground">{{ label(item.execution) }}</p></TableCell>
          <TableCell><Badge :variant="item.settlement === 'PendingAdjudication' ? 'outline' : 'secondary'">{{ label(item.settlement) }}</Badge><p v-if="item.finalScheduledCheck" class="mt-1 text-xs text-muted-foreground">{{ $t('administration.awd.review.finalCheck') }}</p></TableCell>
          <TableCell>{{ label(item.reason) }}</TableCell><TableCell><Button size="sm" variant="outline" :disabled="saving" @click="openEvidence(item)">{{ $t('administration.awd.review.evidence') }}</Button></TableCell>
        </TableRow></TableBody>
      </Table>
      <OffsetPagination :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading || saving" @update:page="loadPage" @update:limit="setPageSize" />
    </CardContent>
  </Card>
  <Dialog v-model:open="detailOpen">
    <DialogScrollContent class="sm:max-w-5xl">
      <DialogHeader><DialogTitle>{{ $t('administration.awd.review.detailTitle') }}</DialogTitle><DialogDescription>{{ $t('administration.awd.review.detailHint') }}</DialogDescription></DialogHeader>
      <div class="flex flex-wrap items-center justify-between gap-3"><p>{{ teamName(selected?.teamId) }} · {{ challengeTitle(selected?.competitionChallengeId) }} · {{ $t('administration.awd.review.round') }} {{ selected?.roundNumber }}</p><Button size="sm" variant="outline" :disabled="detailLoading || saving" @click="loadEvidence"><Spinner v-if="detailLoading" data-icon="inline-start" />{{ $t('common.label.refresh') }}</Button></div>
      <FieldDescription v-if="detailError" class="text-destructive">{{ $message(detailError) }}</FieldDescription>
      <FieldDescription v-if="needsReload">{{ $t('administration.awd.review.reload') }}</FieldDescription>
      <Skeleton v-if="detailLoading" class="h-40 w-full" />
      <template v-else-if="evidence">
        <div class="flex flex-wrap gap-2"><Badge>{{ label(evidence.health) }}</Badge><Badge variant="secondary">{{ label(evidence.execution) }}</Badge><Badge variant="outline">{{ label(evidence.settlement) }}</Badge><Badge variant="secondary">{{ $t(evidence.finalScheduledCheck ? 'administration.awd.review.finalCheck' : 'administration.awd.review.intermediateCheck') }}</Badge></div>
        <p class="text-sm">{{ $t('administration.awd.review.reason') }}: {{ label(evidence.reason) }}<span v-if="evidence.knownTargetFailure"> · {{ $t('administration.awd.review.knownTargetFailure') }}: {{ label(evidence.knownTargetFailure) }}</span></p>
        <dl class="grid gap-3 text-sm sm:grid-cols-3">
          <div v-for="time in evidenceTimes" :key="time.key"><dt class="text-muted-foreground">{{ time.label }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(time.value) }}</dd></div>
        </dl>
        <p class="break-all text-xs text-muted-foreground">{{ $t('administration.awd.review.attempt') }} {{ evidence.attempt }} · {{ evidence.attemptId ?? '—' }}</p>
        <Separator />
        <div class="grid gap-3 text-sm sm:grid-cols-2">
          <p>{{ $t('administration.awd.review.connection') }}: {{ label(evidence.connection?.outcome) }} · {{ label(evidence.connection?.reason) }}</p>
          <p>{{ $t('administration.awd.review.functionality') }}: {{ label(evidence.functionality?.outcome) }} · {{ label(evidence.functionality?.reason) }}</p>
          <p>{{ $t('administration.awd.review.manifestComplete') }}: {{ $t(evidence.manifestComplete ? 'administration.awd.review.complete' : 'administration.awd.review.incomplete') }}</p>
          <p>{{ $t('administration.awd.review.evidenceComplete') }}: {{ $t(evidence.evidenceComplete ? 'administration.awd.review.complete' : 'administration.awd.review.incomplete') }}</p>
        </div>
        <p class="text-sm">{{ $t('administration.awd.review.readable') }}: {{ evidence.readableFlags ?? '—' }} / {{ evidence.requiredFlags ?? '—' }}</p>
        <Table v-if="evidence.flags?.length">
          <TableHeader><TableRow><TableHead>{{ $t('administration.awd.review.flag') }}</TableHead><TableHead>{{ $t('administration.awd.review.age') }}</TableHead><TableHead>{{ $t('administration.awd.review.obligation') }}</TableHead><TableHead>{{ $t('administration.awd.review.write') }}</TableHead><TableHead>{{ $t('administration.awd.review.read') }}</TableHead></TableRow></TableHeader>
          <TableBody><TableRow v-for="flag in evidence.flags" :key="flag.flagId"><TableCell class="max-w-52 break-all font-mono text-xs">{{ flag.flagId }}</TableCell><TableCell>{{ $t(flag.historical ? 'administration.awd.review.historical' : 'administration.awd.review.current') }}</TableCell><TableCell>{{ label(flag.obligation) }}</TableCell><TableCell>{{ label(flag.write?.outcome) }}<p class="text-xs text-muted-foreground">{{ label(flag.write?.reason) }}</p></TableCell><TableCell>{{ label(flag.read?.outcome) }}<p class="text-xs text-muted-foreground">{{ label(flag.read?.reason) }}</p></TableCell></TableRow></TableBody>
        </Table>
        <template v-if="canJudge && evidence.settlement === 'PendingAdjudication'">
          <Separator />
          <FieldGroup>
            <Field><FieldLabel>{{ $t('administration.awd.review.action') }}</FieldLabel><Select v-model="action" :disabled="saving || needsReload"><SelectTrigger :aria-label="$t('administration.awd.review.action')"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="AcceptEvidence" :disabled="!canAccept">{{ $t('administration.awd.review.accept') }}</SelectItem><SelectItem value="Exempt">{{ $t('administration.awd.review.exempt') }}</SelectItem></SelectContent></Select><FieldDescription>{{ $t(action === 'Exempt' ? 'administration.awd.review.exemptHint' : 'administration.awd.review.acceptHint') }}</FieldDescription><FieldDescription v-if="!canAccept">{{ $t('administration.awd.review.cannotAccept') }}</FieldDescription></Field>
            <Field><FieldLabel>{{ $t('administration.awd.review.auditReason') }}</FieldLabel><Textarea v-model="reason" :disabled="saving || needsReload" :maxlength="512" :aria-label="$t('administration.awd.review.auditReason')" /></Field>
          </FieldGroup>
          <DialogFooter><Button :disabled="!canAdjudicate" @click="adjudicate"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('administration.awd.review.apply') }}</Button></DialogFooter>
        </template>
      </template>
    </DialogScrollContent>
  </Dialog>
</template>
