<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformLogsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformLogsPage'

const viewProps = defineProps<{ state: AdminPlatformLogsPageViewState }>()
const { Download, Radio, LEVEL_LABELS, SERVICE_LABELS, levelOrdinal, minimumLevel, service, search, from, to, exporting, items, loading, listError, page, pageLimit, hasPrevious, hasNext, initialized, loadPage, setPageSize, newLogs, viewLatest, applyFilters, live, hubStateBadge, exportLogs, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-end gap-4">
      <Field>
        <FieldLabel for="log-level">{{ $t('administration.label.lowestLevel') }}</FieldLabel>
        <Select v-model="minimumLevel">
          <SelectTrigger id="log-level" class="w-32">
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
      <Field>
        <FieldLabel for="log-service">{{ $t('administration.label.service') }}</FieldLabel>
        <Select v-model="service">
          <SelectTrigger id="log-service" class="w-32">
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
      <Field>
        <FieldLabel for="log-from">{{ $t('administration.label.startTime') }}</FieldLabel>
        <DateTimePicker id="log-from" v-model="from"  />
      </Field>
      <Field>
        <FieldLabel for="log-to">{{ $t('common.label.endTime') }}</FieldLabel>
        <DateTimePicker id="log-to" v-model="to"  />
      </Field>
      <Field class="min-w-56 flex-1">
        <FieldLabel for="log-search">{{ $t('administration.label.search') }}</FieldLabel>
        <Input id="log-search" v-model="search" :placeholder="$t('administration.label.messageCategoryKeyword')" @keyup.enter="applyFilters" />
      </Field>
      <div class="flex items-center gap-2">
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

    <Alert v-if="listError" variant="destructive">
      <AlertDescription>{{ $message(listError.message) }}</AlertDescription>
    </Alert>

    <Card v-if="loading && items.length === 0">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 8" :key="i" class="h-8 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('administration.label.matchingLogs') }}</EmptyTitle>
        <EmptyDescription>{{ $t('administration.platformLogs.description.adjustLevelTimeRange') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-44">{{ $t('administration.label.time') }}</TableHead>
            <TableHead class="w-20">{{ $t('administration.label.level') }}</TableHead>
            <TableHead class="w-20">{{ $t('administration.label.service') }}</TableHead>
            <TableHead class="w-56">{{ $t('administration.label.category.logsPageView') }}</TableHead>
            <TableHead>{{ $t('common.label.news') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="log in items" :key="log.cursor">
            <TableCell class="text-sm">
              <component :is="AdminDateTime" :value="log.timestamp" />
            </TableCell>
            <TableCell>
              <Badge :variant="levelOrdinal(log.level) >= 4 ? 'destructive' : log.level === 'Warning' ? 'secondary' : 'outline'">
                {{ LEVEL_LABELS[String(log.level)] ? translate(LEVEL_LABELS[String(log.level)]!) : log.level }}
              </Badge>
            </TableCell>
            <TableCell class="text-muted-foreground">{{ SERVICE_LABELS[String(log.service)] ? translate(SERVICE_LABELS[String(log.service)]!) : log.service }}</TableCell>
            <Hint :content="log.category" ><TableCell tabindex="0" class="max-w-56 truncate font-mono text-xs text-muted-foreground" >
              {{ log.category }}
            </TableCell></Hint>
            <TableCell class="max-w-xl">
              <Hint :content="log.message" ><div tabindex="0" class="truncate" >{{ $message(log.message) }}</div></Hint>
              <Hint :content="log.exceptionMessage ?? ''" v-if="log.exceptionType"><div tabindex="0"  class="truncate text-xs text-destructive" >
                {{ log.exceptionType }}: {{ log.exceptionMessage }}
              </div></Hint>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <CursorPagination :page="page" :count="items.length" :limit="pageLimit" :loading="loading"
      :has-previous="hasPrevious" :has-next="hasNext" @update:page="loadPage" @update:limit="setPageSize" />
  </div>
</template>
