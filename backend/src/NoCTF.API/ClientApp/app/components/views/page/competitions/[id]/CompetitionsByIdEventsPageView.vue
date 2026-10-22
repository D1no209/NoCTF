<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdEventsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdEventsPage'

const viewProps = defineProps<{ state: CompetitionsByIdEventsPageViewState }>()
const { embedded, ArrowLeft, competitionReturnPath, competitionReturnLabel, hasStaffHistory, historyScopeError, kind, kindOptions, items, loading, error, initialized, page, pageCount, total, pageLimit, loadPage, setPageSize, reload, levelVariant, levelLabel } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <h2 v-if="embedded" class="text-display text-xl">{{ $t('administration.label.activity') }}</h2>
    <div v-else>
      <Button variant="ghost" size="sm" as-child>
        <NuxtLink :to="competitionReturnPath">
          <ArrowLeft data-icon="inline-start" />{{ $t(competitionReturnLabel) }}
        </NuxtLink>
      </Button>
    </div>
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-wrap items-center gap-3">
        <Select v-model="kind">
          <SelectTrigger class="w-40" :aria-label="$t('competitions.label.updates')">
            <SelectValue :placeholder="$t('competitions.label.updates')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ translate(option.label) }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Badge variant="outline">
          {{ $t(hasStaffHistory ? 'common.label.fullHistory' : 'common.label.lastDays') }}
        </Badge>
      </div>
      <Button variant="outline" size="sm" :disabled="loading" @click="reload"><Spinner v-if="loading" data-icon="inline-start" />{{ $t('common.label.refresh') }}</Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>

    <Alert v-if="historyScopeError" variant="destructive">
      <AlertDescription>{{ $message(historyScopeError) }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 6" :key="i" class="h-12 w-full" />
    </div>

    <Empty v-else-if="!error && initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('competitions.label.newsYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('competitions.competitionsBy.description.announcementsTopicReleasesBlood') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else class="min-w-[640px] [&_th]:px-4 [&_th]:py-3 [&_td]:px-4 [&_td]:py-4">
      <TableHeader class="bg-muted/30">
        <TableRow>
          <TableHead class="w-48">{{ $t('administration.label.time') }}</TableHead>
          <TableHead class="w-24">{{ $t('administration.label.level') }}</TableHead>
          <TableHead>{{ $t('common.label.content') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="event in items" :key="event.id">
          <TableCell class="font-mono text-xs tabular-nums">{{ formatDateTime(event.occurredAt) }}</TableCell>
          <TableCell><Badge :variant="levelVariant(event.level)">{{ levelLabel(event.level) }}</Badge></TableCell>
          <TableCell class="whitespace-normal [overflow-wrap:anywhere]">{{ competitionEventText(event) }}</TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <OffsetPagination
      v-if="initialized"
      :page="page"
      :page-count="pageCount"
      :total="total"
      :limit="pageLimit"
      :loading="loading"
      @update:page="loadPage"
      @update:limit="setPageSize"
    />
  </section>
</template>
