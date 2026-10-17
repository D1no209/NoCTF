<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformAuditPageViewState } from '~/features/routes/admin/platform/useAdminPlatformAuditPage'

const viewProps = defineProps<{ state: AdminPlatformAuditPageViewState }>()
const { adminUserPath, adminTeamPath, adminAuditSubjectPath, Download, platformAuditActionText, KIND_LABELS, kind, actorId, competitionId, from, to, items, loading, listError, hasMore, initialized, loadMore, applyFilters, exportingArchive, exportArchive, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <div class="flex flex-col gap-4">
      <div class="flex flex-wrap items-end gap-4">
        <Field>
          <FieldLabel for="audit-kind">{{ $t('common.label.type') }}</FieldLabel>
          <Select v-model="kind">
            <SelectTrigger id="audit-kind" class="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">{{ $t('administration.label.types') }}</SelectItem>
                <SelectItem value="CompetitionLifecycle">{{ $t('administration.label.competitionLifeCycle') }}</SelectItem>
                <SelectItem value="UserAccountLifecycle">{{ $t('administration.label.accountLifeCycle') }}</SelectItem>
                <SelectItem value="PlatformAdministration">{{ $t('common.label.platformAdmin') }}</SelectItem>
                <SelectItem value="CompetitionAdministration">{{ $t('navigation.competitionAdmin') }}</SelectItem>
                <SelectItem value="CompetitionLeaderboardVisibility">{{ $t('administration.label.listVisibility') }}</SelectItem>
                <SelectItem value="CompetitionEvent">{{ $t('administration.label.competitionEvent') }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel for="audit-actor">{{ $t('administration.label.operatorId') }}</FieldLabel>
          <Input id="audit-actor" v-model="actorId" class="w-64 font-mono text-sm" :placeholder="$t('common.label.optional')" />
        </Field>
        <Field>
          <FieldLabel for="audit-competition">{{ $t('administration.label.contestId') }}</FieldLabel>
          <Input id="audit-competition" v-model="competitionId" class="w-64 font-mono text-sm" :placeholder="$t('common.label.optional')" />
        </Field>
        <Field>
          <FieldLabel for="audit-from">{{ $t('administration.label.startTime') }}</FieldLabel>
          <DateTimePicker id="audit-from" v-model="from"  />
        </Field>
        <Field>
          <FieldLabel for="audit-to">{{ $t('common.label.endTime') }}</FieldLabel>
          <DateTimePicker id="audit-to" v-model="to"  />
        </Field>
        <Button @click="applyFilters">{{ $t('administration.label.query') }}</Button>
        <Button variant="outline" :disabled="exportingArchive" @click="exportArchive">
          <Spinner v-if="exportingArchive" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" />{{ $t('administration.label.downloadAuditArchive') }}
        </Button>
      </div>

      <RecordListSection
        :loading="loading"
        :error-message="listError?.message ?? null"
        :initialized="initialized"
        :item-count="items.length"
        :has-more="hasMore"
        empty-title-key="administration.label.matchingAuditRecord"
        empty-description-key="administration.platformAudit.description.adjustTypeTimeRange"
        :skeleton-count="6"
        skeleton-class="h-10 w-full"
        @load-more="loadMore"
      >
        <template #header>
          <TableHead class="w-44">{{ $t('administration.label.time') }}</TableHead>
          <TableHead class="w-32">{{ $t('common.label.type') }}</TableHead>
          <TableHead>{{ $t('administration.label.subject') }}</TableHead>
          <TableHead>{{ $t('common.label.actions') }}</TableHead>
          <TableHead class="w-32">{{ $t('administration.label.operator') }}</TableHead>
        </template>
        <template #rows>
          <TableRow v-for="log in items" :key="log.id">
            <TableCell class="text-sm">
              <component :is="AdminDateTime" :value="log.occurredAt" />
            </TableCell>
            <TableCell>
              <Badge variant="secondary">{{ KIND_LABELS[String(log.kind)] ? translate(KIND_LABELS[String(log.kind)]!) : log.kind }}</Badge>
              <Badge v-if="log.automatic" variant="outline" class="ml-1">{{ $t('administration.label.automatic') }}</Badge>
            </TableCell>
            <TableCell class="max-w-56">
              <Hint :content="log.subjectId" ><NuxtLink v-if="adminAuditSubjectPath(log)" :to="adminAuditSubjectPath(log)" class="block truncate hover:underline">{{ log.subjectDisplayName ?? log.subjectId }}</NuxtLink><div v-else tabindex="0" class="truncate">{{ log.subjectDisplayName ?? log.subjectId }}</div></Hint>
            </TableCell>
            <TableCell class="max-w-md">
              <Hint :content="platformAuditActionText(log)" ><div tabindex="0" class="font-medium" >{{ platformAuditActionText(log) }}</div></Hint>
              <div v-if="log.fileId" class="mt-1 flex flex-col gap-1 break-all font-mono text-xs text-muted-foreground">
                <span>{{ $t('administration.label.fileId') }}: {{ log.fileId }}</span>
                <span>{{ $t('common.label.submissionId') }}: {{ log.gameplayFactId }}</span>
                <span>{{ $t('common.label.teamId') }}: <NuxtLink v-if="adminTeamPath(log.competitionId, log.teamId)" :to="adminTeamPath(log.competitionId, log.teamId)" class="hover:underline">{{ log.teamId }}</NuxtLink><span v-else>{{ log.teamId }}</span></span>
              </div>
              <div v-if="log.jwtId || log.tokenExpiresAt || log.tokenVersion !== null && log.tokenVersion !== undefined" class="mt-1 flex flex-col gap-1 break-all font-mono text-xs text-muted-foreground">
                <span v-if="log.jwtId">{{ $t('administration.label.jwtId') }}: {{ log.jwtId }}</span>
                <span v-if="log.tokenExpiresAt">{{ $t('administration.label.expires') }} <component :is="AdminDateTime" :value="log.tokenExpiresAt" /></span>
                <span v-if="log.tokenVersion !== null && log.tokenVersion !== undefined">{{ $t('administration.label.tokenVersion') }}: {{ log.tokenVersion }}</span>
              </div>
            </TableCell>
            <Hint :content="log.actorId ?? ''" ><TableCell tabindex="0" class="max-w-32 truncate font-mono text-xs text-muted-foreground" >
              <NuxtLink v-if="log.actorId" :to="adminUserPath(log.actorId)" class="hover:underline">{{ log.actorId }}</NuxtLink><span v-else>{{ $t('common.label.system') }}</span>
            </TableCell></Hint>
          </TableRow>
        </template>
      </RecordListSection>
    </div>

  </div>
</template>
