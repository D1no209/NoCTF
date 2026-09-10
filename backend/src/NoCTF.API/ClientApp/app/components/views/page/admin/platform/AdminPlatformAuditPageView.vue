<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformAuditPageViewState } from '~/features/routes/admin/platform/useAdminPlatformAuditPage'

const viewProps = defineProps<{ state: AdminPlatformAuditPageViewState }>()
const { Download, platformAuditActionText, KIND_LABELS, kind, actorId, competitionId, from, to, items, loading, listError, hasMore, initialized, loadMore, applyFilters, exportingArchive, exportArchive, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <div class="flex flex-col gap-4">
      <div class="flex flex-wrap items-end gap-4">
        <Field>
          <FieldLabel for="audit-kind">{{ $t('ui.type') }}</FieldLabel>
          <Select v-model="kind">
            <SelectTrigger id="audit-kind" class="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">{{ $t('ui.allTypes') }}</SelectItem>
                <SelectItem value="CompetitionLifecycle">{{ $t('ui.competitionLifeCycle') }}</SelectItem>
                <SelectItem value="UserAccountLifecycle">{{ $t('ui.accountLifeCycle') }}</SelectItem>
                <SelectItem value="PlatformAdministration">{{ $t('ui.platformAdmin') }}</SelectItem>
                <SelectItem value="CompetitionAdministration">{{ $t('ui.competitionAdmin') }}</SelectItem>
                <SelectItem value="CompetitionLeaderboardVisibility">{{ $t('ui.listVisibility') }}</SelectItem>
                <SelectItem value="CompetitionEvent">{{ $t('ui.competitionEvent') }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel for="audit-actor">{{ $t('ui.operatorId') }}</FieldLabel>
          <Input id="audit-actor" v-model="actorId" class="w-64 font-mono text-sm" :placeholder="$t('ui.optional')" />
        </Field>
        <Field>
          <FieldLabel for="audit-competition">{{ $t('ui.contestId') }}</FieldLabel>
          <Input id="audit-competition" v-model="competitionId" class="w-64 font-mono text-sm" :placeholder="$t('ui.optional')" />
        </Field>
        <Field>
          <FieldLabel for="audit-from">{{ $t('ui.startTime2') }}</FieldLabel>
          <DateTimePicker id="audit-from" v-model="from"  />
        </Field>
        <Field>
          <FieldLabel for="audit-to">{{ $t('ui.endTime') }}</FieldLabel>
          <DateTimePicker id="audit-to" v-model="to"  />
        </Field>
        <Button @click="applyFilters">{{ $t('ui.query') }}</Button>
        <Button variant="outline" :disabled="exportingArchive" @click="exportArchive">
          <Spinner v-if="exportingArchive" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" />{{ $t('ui.downloadAuditArchive') }}
        </Button>
      </div>

      <Alert v-if="listError" variant="destructive">
        <AlertDescription>{{ $message(listError.message) }}</AlertDescription>
      </Alert>

      <Card v-if="loading && items.length === 0">
        <CardContent class="flex flex-col gap-3 pt-6">
          <Skeleton v-for="i in 6" :key="i" class="h-10 w-full" />
        </CardContent>
      </Card>

      <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.noMatchingAuditRecord') }}</EmptyTitle>
          <EmptyDescription>{{ $t('ui.adjustTheTypeTimeRangeOrFilters') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <Card v-else-if="items.length > 0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-44">{{ $t('ui.time') }}</TableHead>
              <TableHead class="w-32">{{ $t('ui.type') }}</TableHead>
              <TableHead>{{ $t('ui.subject') }}</TableHead>
              <TableHead>{{ $t('ui.actions') }}</TableHead>
              <TableHead class="w-32">{{ $t('ui.operator') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="log in items" :key="log.id">
              <TableCell class="text-sm">
                <component :is="AdminDateTime" :value="log.occurredAt" />
              </TableCell>
              <TableCell>
                <Badge variant="secondary">{{ KIND_LABELS[String(log.kind)] ? $t(KIND_LABELS[String(log.kind)]!) : log.kind }}</Badge>
                <Badge v-if="log.automatic" variant="outline" class="ml-1">{{ $t('ui.automatic') }}</Badge>
              </TableCell>
              <TableCell class="max-w-56">
                <Hint :content="log.subjectId" ><div tabindex="0" class="truncate" >{{ log.subjectDisplayName ?? log.subjectId }}</div></Hint>
              </TableCell>
              <TableCell class="max-w-md">
                <Hint :content="platformAuditActionText(log)" ><div tabindex="0" class="font-medium" >{{ platformAuditActionText(log) }}</div></Hint>
                <div v-if="log.fileId" class="mt-1 flex flex-col gap-1 break-all font-mono text-xs text-muted-foreground">
                  <span>{{ $t('ui.fileId') }}: {{ log.fileId }}</span>
                  <span>{{ $t('ui.submissionId2') }}: {{ log.gameplayFactId }}</span>
                  <span>{{ $t('ui.teamId') }}: {{ log.teamId }}</span>
                </div>
              </TableCell>
              <Hint :content="log.actorId ?? ''" ><TableCell tabindex="0" class="max-w-32 truncate font-mono text-xs text-muted-foreground" >
                {{ log.actorId ?? $t('ui.system') }}
              </TableCell></Hint>
            </TableRow>
          </TableBody>
        </Table>
      </Card>

      <AdminLoadMore :loading="loading" :has-more="hasMore" @load="loadMore" />
    </div>

  </div>
</template>
