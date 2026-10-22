<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformLogsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformLogsPage'

const viewProps = defineProps<{ state: AdminPlatformLogsPageViewState }>()
const { Download, Radio, levelLabel, serviceLabel, levelOrdinal, minimumLevel, service, search, from, to, exporting, items, loading, listError, hasMore, initialized, loadMore, newLogs, viewLatest, applyFilters, live, hubStateBadge, exportLogs, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <div class="grid grid-cols-1 items-end gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Field class="min-w-0">
        <FieldLabel for="log-level">{{ $t('administration.label.lowestLevel') }}</FieldLabel>
        <Select v-model="minimumLevel">
          <SelectTrigger id="log-level" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Trace">{{ $t('administration.label.track') }}</SelectItem>
              <SelectItem value="Debug">{{ $t('administration.label.debugging') }}</SelectItem>
              <SelectItem value="Information">{{ $t('common.label.information') }}</SelectItem>
              <SelectItem value="Warning">{{ $t('common.label.warning') }}</SelectItem>
              <SelectItem value="Error">{{ $t('common.label.wrong') }}</SelectItem>
              <SelectItem value="Critical">{{ $t('administration.label.serious') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field class="min-w-0">
        <FieldLabel for="log-service">{{ $t('administration.label.service') }}</FieldLabel>
        <Select v-model="service">
          <SelectTrigger id="log-service" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="all">{{ $t('administration.label.platformLogs') }}</SelectItem>
              <SelectItem value="Api">{{ $t('administration.label.api') }}</SelectItem>
              <SelectItem value="Worker">{{ $t('administration.label.worker') }}</SelectItem>
              <SelectItem value="Runner">{{ $t('common.label.runner') }}</SelectItem>
              <SelectItem value="Host">{{ $t('administration.label.host.platformLogsPage') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field class="min-w-0">
        <FieldLabel for="log-from">{{ $t('administration.label.startTime') }}</FieldLabel>
        <DateTimePicker id="log-from" v-model="from"  />
      </Field>
      <Field class="min-w-0">
        <FieldLabel for="log-to">{{ $t('common.label.endTime') }}</FieldLabel>
        <DateTimePicker id="log-to" v-model="to"  />
      </Field>
      <Field class="min-w-0 sm:col-span-2 xl:col-span-3">
        <FieldLabel for="log-search">{{ $t('administration.label.search') }}</FieldLabel>
        <Input id="log-search" v-model="search" :placeholder="$t('administration.label.messageCategoryKeyword')" @keyup.enter="applyFilters" />
      </Field>
      <div class="flex flex-wrap items-center gap-2 xl:justify-end">
        <Button @click="applyFilters">{{ $t('administration.label.query') }}</Button>
        <Button variant="outline" :disabled="exporting" @click="exportLogs">
          <Spinner v-if="exporting" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" /> {{ $t('administration.label.export') }} </Button>
      </div>
    </div>

    <div class="flex flex-wrap items-center gap-3">
      <Switch id="live-stream" v-model="live" />
      <Label for="live-stream" class="inline-flex items-center gap-1">
        <Radio class="size-4" /> {{ $t('administration.platformLogs.description.receiveNewLogsReal') }} </Label>
      <Badge :variant="hubStateBadge.variant">{{ hubStateBadge.label }}</Badge>
      <Button v-if="newLogs > 0" variant="outline" size="sm" :disabled="loading" @click="viewLatest">
        {{ $t('administration.platformLogs.newLogs', { count: newLogs }) }}
      </Button>
    </div>

    <RecordListSection
      :loading="loading"
      :error-message="listError?.message ?? null"
      :initialized="initialized"
      :item-count="items.length"
      :has-more="hasMore"
      empty-title-key="administration.label.matchingLogs"
      empty-description-key="administration.platformLogs.description.adjustLevelTimeRange"
      @load-more="loadMore"
    >
      <template #header>
        <TableHead class="w-44">{{ $t('administration.label.time') }}</TableHead>
        <TableHead class="w-20">{{ $t('administration.label.level') }}</TableHead>
        <TableHead class="w-20">{{ $t('administration.label.service') }}</TableHead>
        <TableHead class="w-56">{{ $t('administration.label.category.logsPageView') }}</TableHead>
        <TableHead class="min-w-64">{{ $t('common.label.news') }}</TableHead>
      </template>
      <template #rows>
        <TableRow v-for="log in items" :key="log.cursor">
          <TableCell class="text-sm">
            <component :is="AdminDateTime" :value="log.timestamp" />
          </TableCell>
          <TableCell>
            <Badge :variant="levelOrdinal(log.level) >= 4 ? 'destructive' : log.level === 'Warning' ? 'secondary' : 'outline'">
              {{ levelLabel(log.level) }}
            </Badge>
          </TableCell>
          <TableCell class="text-muted-foreground">{{ serviceLabel(log.service) }}</TableCell>
          <TableCell class="font-mono text-xs text-muted-foreground"><Hint :content="log.category"><div tabindex="0" class="truncate">
            {{ log.category }}
          </div></Hint></TableCell>
          <TableCell>
            <Hint :content="log.message" ><div tabindex="0" class="truncate" >{{ $message(log.message) }}</div></Hint>
            <Hint :content="log.exceptionMessage ?? ''" v-if="log.exceptionType"><div tabindex="0"  class="truncate text-xs text-destructive" >
              {{ log.exceptionType }}: {{ log.exceptionMessage }}
            </div></Hint>
          </TableCell>
        </TableRow>
      </template>
    </RecordListSection>
  </div>
</template>
