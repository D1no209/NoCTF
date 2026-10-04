<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeCompetitionPlacementsViewState } from '~/features/admin/useChallengeCompetitionPlacements'
const viewProps = defineProps<{ state: ChallengeCompetitionPlacementsViewState }>()
const { ArrowUpRight, ChevronLeft, ChevronRight, Plus, RefreshCw, loading, error, adding, linked, linkedOptions, candidates, preview, previewPosterUrl, previewMotion, targetIndex, targetId, canAdd, previousTarget, nextTarget, selectTarget, load, addToCompetition } = toRefs(viewProps.state)
</script>

<template>
  <Card class="gap-0">
    <CardHeader class="flex shrink-0 flex-row items-center justify-between gap-3">
      <CardTitle>{{ $t('placements.title') }}</CardTitle>
      <Button type="button" variant="outline" size="sm" :disabled="loading || adding" @click="load">
        <RefreshCw data-icon="inline-start" />{{ $t('runtime.label.refreshStatus') }}
      </Button>
    </CardHeader>
    <ScrollSurface axis="y" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
      <CardContent class="flex flex-col gap-5">
        <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
        <template v-if="loading"><Skeleton class="h-56 w-full" /><Skeleton class="h-24 w-full" /></template>
        <template v-else>
          <section :aria-label="$t('placements.linked')">
            <div class="mb-3 flex items-center gap-2"><h3 class="text-sm font-semibold">{{ $t('placements.linked') }}</h3><Badge variant="secondary">{{ linked.length }}</Badge></div>
            <div v-if="linkedOptions.length" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              <Button v-for="item in linkedOptions" :key="item.value ?? undefined" as-child variant="ghost" class="h-auto min-w-0 overflow-hidden p-0">
                <NuxtLink :to="item.href" :aria-label="$t('placements.manageChallenge', { competition: item.label })" class="block w-full">
                  <CoverImage :src="item.posterUrl" :alt="item.label" :aspect-ratio="12 / 5" class="w-full">
                    <div class="flex min-h-full items-end justify-between gap-2 p-4">
                      <span class="min-w-0 truncate text-left text-sm font-semibold">{{ item.label }}</span>
                      <ArrowUpRight class="size-4 shrink-0" aria-hidden="true" />
                    </div>
                  </CoverImage>
                </NuxtLink>
              </Button>
            </div>
            <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('placements.noneLinked') }}</EmptyTitle></EmptyHeader></Empty>
          </section>
          <Separator />
          <section class="flex min-w-0 flex-col gap-3">
            <h3 class="text-sm font-semibold">{{ $t('placements.quickAdd') }}</h3>
            <template v-if="candidates.length">
              <div class="grid min-w-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3">
                <Button type="button" variant="ghost" size="icon" :aria-label="$t('placements.previous')" :disabled="adding || targetIndex <= 0" @click="previousTarget"><ChevronLeft aria-hidden="true" /></Button>
                <section class="min-w-0 overflow-hidden rounded-md" :aria-label="$t('placements.posterPreview')">
                  <MotionSwap :identity="preview?.competition.id ?? ''" :preset="previewMotion">
                    <CoverImage v-if="preview" :src="previewPosterUrl" :alt="preview.competition.title ?? $t('common.label.competitionPoster')" :aspect-ratio="16 / 7">
                      <div class="flex min-h-full flex-col justify-end gap-2 p-5">
                        <h3 class="text-left text-lg font-semibold">{{ preview.competition.title }}</h3>
                      </div>
                    </CoverImage>
                  </MotionSwap>
                </section>
                <Button type="button" variant="ghost" size="icon" :aria-label="$t('placements.next')" :disabled="adding || targetIndex >= candidates.length - 1" @click="nextTarget"><ChevronRight aria-hidden="true" /></Button>
              </div>

            </template>
            <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('placements.noneAvailable') }}</EmptyTitle></EmptyHeader></Empty>
          </section>
        </template>
      </CardContent>
    </ScrollSurface>
    <CardFooter v-if="!loading && candidates.length" class="shrink-0 pt-3">
              <div class="flex w-full flex-wrap items-end gap-3">
                <Field class="min-w-0 flex-1">
                  <FieldLabel for="placement-competition">{{ $t('placements.chooseCompetition') }}</FieldLabel>
                  <Select :model-value="targetId" :disabled="adding" @update:model-value="selectTarget">
                    <SelectTrigger id="placement-competition" class="w-full"><SelectValue :placeholder="$t('placements.chooseCompetition')" /></SelectTrigger>
                    <SelectContent><SelectGroup><SelectItem v-for="candidate in candidates" :key="candidate.competition.id ?? undefined" :value="candidate.competition.id">{{ candidate.competition.title }}</SelectItem></SelectGroup></SelectContent>
                  </Select>
                </Field>
                <span class="pb-2 font-mono text-xs tabular-nums text-muted-foreground">{{ targetIndex + 1 }} / {{ candidates.length }}</span>
                <Button type="button" :disabled="!canAdd" @click="addToCompetition"><Spinner v-if="adding" data-icon="inline-start" /><Plus v-else data-icon="inline-start" />{{ $t('placements.add') }}</Button>
              </div>
    </CardFooter>
  </Card>
</template>
