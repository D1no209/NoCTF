<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformLogsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformLogsPage'

const viewProps = defineProps<{ state: AdminPlatformLogsPageViewState }>()
const { Download, Radio, LEVEL_LABELS, SERVICE_LABELS, levelOrdinal, minimumLevel, service, search, from, to, exporting, items, loading, listError, hasMore, initialized, loadMore, applyFilters, live, hubStateBadge, exportLogs, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-end gap-4">
      <Field>
        <FieldLabel for="log-level">{{ $t('ui.lowestLevel') }}</FieldLabel>
        <Select v-model="minimumLevel">
          <SelectTrigger id="log-level" class="w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="Trace">{{ $t('ui.track') }}</SelectItem>
              <SelectItem value="Debug">{{ $t('ui.debugging') }}</SelectItem>
              <SelectItem value="Information">{{ $t('ui.information') }}</SelectItem>
              <SelectItem value="Warning">{{ $t('ui.warning') }}</SelectItem>
              <SelectItem value="Error">{{ $t('ui.wrong') }}</SelectItem>
              <SelectItem value="Critical">{{ $t('ui.serious') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="log-service">{{ $t('ui.service') }}</FieldLabel>
        <Select v-model="service">
          <SelectTrigger id="log-service" class="w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="all">{{ $t('ui.all') }}</SelectItem>
              <SelectItem value="Api">{{ $t('ui.api') }}</SelectItem>
              <SelectItem value="Worker">{{ $t('ui.worker') }}</SelectItem>
              <SelectItem value="Runner">{{ $t('ui.runner') }}</SelectItem>
              <SelectItem value="Host">{{ $t('ui.host3') }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="log-from">{{ $t('ui.startTime2') }}</FieldLabel>
        <DateTimePicker id="log-from" v-model="from"  />
      </Field>
      <Field>
        <FieldLabel for="log-to">{{ $t('ui.endTime') }}</FieldLabel>
        <DateTimePicker id="log-to" v-model="to"  />
      </Field>
      <Field class="min-w-56 flex-1">
        <FieldLabel for="log-search">{{ $t('ui.search') }}</FieldLabel>
        <Input id="log-search" v-model="search" :placeholder="$t('ui.messageOrCategoryKeyword')" @keyup.enter="applyFilters" />
      </Field>
      <div class="flex items-center gap-2">
        <Button @click="applyFilters">{{ $t('ui.query') }}</Button>
        <Button variant="outline" :disabled="exporting" @click="exportLogs">
          <Spinner v-if="exporting" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" /> {{ $t('ui.export') }} </Button>
      </div>
    </div>

    <div class="flex items-center gap-3">
      <Switch id="live-stream" v-model="live" />
      <Label for="live-stream" class="inline-flex items-center gap-1">
        <Radio class="size-4" /> {{ $t('ui.receiveNewLogsInRealTime') }} </Label>
      <Badge :variant="hubStateBadge.variant">{{ hubStateBadge.label }}</Badge>
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
        <EmptyTitle>{{ $t('ui.noMatchingLogs') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.adjustLevelTimeRangeOrSearchKeywords') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-44">{{ $t('ui.time') }}</TableHead>
            <TableHead class="w-20">{{ $t('ui.level') }}</TableHead>
            <TableHead class="w-20">{{ $t('ui.service') }}</TableHead>
            <TableHead class="w-56">{{ $t('ui.category2') }}</TableHead>
            <TableHead>{{ $t('ui.news') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="log in items" :key="log.cursor">
            <TableCell class="text-sm">
              <component :is="AdminDateTime" :value="log.timestamp" />
            </TableCell>
            <TableCell>
              <Badge :variant="levelOrdinal(log.level) >= 4 ? 'destructive' : log.level === 'Warning' ? 'secondary' : 'outline'">
                {{ LEVEL_LABELS[String(log.level)] ? $t(LEVEL_LABELS[String(log.level)]!) : log.level }}
              </Badge>
            </TableCell>
            <TableCell class="text-muted-foreground">{{ SERVICE_LABELS[String(log.service)] ? $t(SERVICE_LABELS[String(log.service)]!) : log.service }}</TableCell>
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

    <AdminLoadMore :loading="loading" :has-more="hasMore" @load="loadMore" />
  </div>
</template>
