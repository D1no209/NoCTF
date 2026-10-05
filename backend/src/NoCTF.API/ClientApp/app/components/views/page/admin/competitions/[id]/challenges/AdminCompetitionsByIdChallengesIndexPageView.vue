<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdChallengesIndexPageViewState } from '~/features/routes/admin/competitions/[id]/challenges/useAdminCompetitionsByIdChallengesIndexPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdChallengesIndexPageViewState }>()
const { Plus, competitionId, competition, canWrite, items, loading, error, includeDeleted, search, directionFilter, directionOptions, statusFilter, filteredItems, pageItems, page, pageLimit, pageCount, total, loadPage, setPageSize, pendingId, addOpen, templatesLoading, selectedTemplateId, templateSearch, hideAddedTemplates, newCustomTitle, newTags, tagOptions, updateNewTags, newOrder, adding, addError, modeTemplates, visibleModeTemplates, openAdd, addChallenge, deleteTarget, deletePending, deleteError, closeDeleteDialog, beginDeleteChallenge, removeChallenge, restoreChallenge, setChallengePublished, onClickAddOpen } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-col gap-3 xl:flex-row xl:items-end xl:justify-between">
      <div class="grid min-w-0 flex-1 gap-3 sm:grid-cols-2 lg:grid-cols-[minmax(16rem,1fr)_12rem_12rem]">
        <Field>
          <FieldLabel for="competition-challenge-search">{{ $t('administration.label.search') }}</FieldLabel>
          <Input
            id="competition-challenge-search"
            v-model="search"
            :placeholder="$t('administration.label.searchCompetitionChallenges')"
          />
        </Field>
        <Field>
          <FieldLabel for="competition-challenge-direction">{{ $t('administration.label.category') }}</FieldLabel>
          <Select id="competition-challenge-direction" v-model="directionFilter">
            <SelectTrigger class="w-full"><SelectValue /></SelectTrigger>
            <SelectContent position="popper">
              <SelectGroup>
                <SelectItem value="all">{{ $t('administration.label.directions') }}</SelectItem>
                <SelectItem v-for="option in directionOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel for="competition-challenge-status">{{ $t('common.label.status') }}</FieldLabel>
          <Select id="competition-challenge-status" v-model="statusFilter">
            <SelectTrigger class="w-full"><SelectValue /></SelectTrigger>
            <SelectContent position="popper">
              <SelectGroup>
                <SelectItem value="all">{{ $t('common.label.status.runtimesPageView') }}</SelectItem>
                <SelectItem value="published">{{ $t('administration.label.published') }}</SelectItem>
                <SelectItem value="unpublished">{{ $t('administration.label.unpublished') }}</SelectItem>
                <SelectItem value="deleted" :disabled="!includeDeleted">{{ $t('common.label.deleted.competitionSidebarView') }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
      </div>
      <div class="flex flex-wrap items-center justify-between gap-3 xl:justify-end">
        <div class="flex h-10 items-center gap-2">
          <Checkbox id="show-deleted" v-model="includeDeleted" />
          <Label for="show-deleted" class="text-sm text-muted-foreground">{{ $t('administration.label.showDeleted') }}</Label>
        </div>
        <Button v-if="canWrite" size="sm" @click="openAdd">
          <Plus data-icon="inline-start" /> {{ $t('administration.label.addQuestionBank') }}
        </Button>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-48 w-full" />

    <Empty v-else-if="items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('administration.label.titleYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('administration.competitionsBy.description.startConfiguringCompetitionAdding') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Empty v-else-if="filteredItems.length === 0" class="py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('administration.label.matchingCompetitionChallenges') }}</EmptyTitle>
        <EmptyDescription>{{ $t('administration.platformUsers.label.adjustSearchFilters') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead class="w-16">{{ $t('administration.label.order') }}</TableHead>
          <TableHead>{{ $t('common.label.title') }}</TableHead>
          <TableHead>{{ $t('administration.label.category') }}</TableHead>
          <TableHead class="w-28">{{ $t('common.label.status') }}</TableHead>
          <TableHead v-if="canWrite" class="w-40 text-right">{{ $t('common.label.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="c in pageItems" :key="c.id" :class="{ 'opacity-60': c.deletedAt }">
          <TableCell class="font-mono tabular-nums">{{ c.order }}</TableCell>
          <TableCell>
            <NuxtLink
              v-if="!c.deletedAt"
              class="font-medium underline-offset-4 hover:underline"
              :to="`/admin/competitions/${competitionId}/challenges/${c.id}`"
              prefetch-on="interaction"
            >
              {{ c.title }}
            </NuxtLink>
            <span v-else class="font-medium">{{ c.title }}</span>
          </TableCell>
          <TableCell>
            <Badge variant="outline" :class="directionBadgeClass(c.direction)">
              <LucideIcon v-if="c.directionIcon" :name="c.directionIcon" />{{ c.direction || '' }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge v-if="c.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
            <div v-else-if="canWrite" class="flex items-center gap-2">
              <Spinner v-if="pendingId === c.id" class="size-4" />
              <Switch
                v-else
                :id="`challenge-published-${c.id}`"
                :model-value="c.isPublished"
                :disabled="pendingId !== null"
                :aria-label="c.isPublished ? $t('administration.label.unpublishCompetitionChallenge') : $t('administration.label.publishCompetitionChallenge')"
                @update:model-value="setChallengePublished(c, $event)"
              />
              <FieldLabel :for="`challenge-published-${c.id}`" class="whitespace-nowrap text-xs">
                {{ c.isPublished ? $t('administration.label.published') : $t('administration.label.unpublished') }}
              </FieldLabel>
            </div>
            <Badge v-else :variant="c.isPublished ? 'default' : 'outline'">
              {{ c.isPublished ? $t('administration.label.published') : $t('administration.label.unpublished') }}
            </Badge>
          </TableCell>
          <TableCell v-if="canWrite" class="text-right">
            <div class="flex justify-end gap-1">
              <Button
                v-if="c.deletedAt"
                variant="outline"
                size="sm"
                :disabled="pendingId !== null"
                @click="restoreChallenge(c)"
              >
                <Spinner v-if="pendingId === c.id" data-icon="inline-start" /> {{ $t('administration.label.restore') }} </Button>
              <Button
                v-else
                variant="ghost"
                size="sm"
                :disabled="pendingId !== null"
                @click="beginDeleteChallenge(c)"
              > {{ $t('common.action.delete') }} </Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <OffsetPagination
      v-if="filteredItems.length > 0"
      :page="page"
      :page-count="pageCount"
      :total="total"
      :limit="pageLimit"
      :loading="loading"
      @update:page="loadPage"
      @update:limit="setPageSize"
    />

    <Dialog v-model:open="addOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('administration.competitionsBy.label.addQuestionsQuestionBank') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.competitionsBy.description.selectQuestionBankTemplate.indexPageView') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="addError" variant="destructive">
            <AlertDescription>{{ $message(addError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="tpl">{{ $t('administration.label.questionBankTemplate') }}</FieldLabel>
            <Skeleton v-if="templatesLoading" class="h-9 w-full" />
            <template v-else>
              <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
                <Input
                  id="template-search"
                  v-model="templateSearch"
                  class="flex-1"
                  :placeholder="$t('administration.label.searchQuestionBankTemplates')"
                  :aria-label="$t('administration.label.searchQuestionBankTemplates')"
                />
                <div class="flex shrink-0 items-center gap-2">
                  <Switch id="hide-added-templates" v-model="hideAddedTemplates" />
                  <FieldLabel for="hide-added-templates" class="cursor-pointer whitespace-nowrap text-xs">
                    {{ $t('administration.label.hideAddedQuestions') }}
                  </FieldLabel>
                </div>
              </div>
              <Select id="tpl" v-model="selectedTemplateId">
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="$t('administration.label.selectTemplate')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem v-for="t in visibleModeTemplates" :key="t.id" :value="t.id!">
                      {{ t.title }}({{ directionLabel(t.direction) }})
                    </SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </template>
            <FieldDescription v-if="!templatesLoading && modeTemplates.length === 0">
              {{ $t('administration.competitionsBy.description.availableTemplatesChallengeLibrary', { mode: enumLabel(GameModeLabel, competition?.mode) }) }}
            </FieldDescription>
            <FieldDescription v-else-if="!templatesLoading && visibleModeTemplates.length === 0">
              {{ $t('administration.competitionsBy.label.matchingQuestionBankTemplates') }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="new-title">{{ $t('administration.label.competitionChallengeName') }}</FieldLabel>
            <Input
              id="new-title"
              v-model="newCustomTitle"
              maxlength="160"
              :placeholder="$t('administration.competitionsBy.description.leaveBlankQuestionBank')"
            />
            <FieldDescription>{{ $t('administration.competitionsBy.description.changesDisplayNameCompetition') }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel>{{ $t('challengeTags.label') }}</FieldLabel>
            <TagPicker :model-value="newTags" :options="tagOptions" :label="$t('challengeTags.edit')" allow-create :disabled="adding" @update:model-value="updateNewTags" />
          </Field>
          <Field>
            <FieldLabel for="new-order">{{ $t('administration.label.order') }}</FieldLabel>
            <NumberInput id="new-order" v-model.number="newOrder"  min="0" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickAddOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="adding || !selectedTemplateId" @click="addChallenge">
            <Spinner v-if="adding" data-icon="inline-start" /> {{ $t('administration.label.add') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteTarget !== null" @update:open="closeDeleteDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.deleteQuestion') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.competitionsBy.description.participantsLongerSeeRestore', { title: deleteTarget?.title ?? '-' }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="deleteError" variant="destructive">
          <AlertDescription>{{ $message(deleteError) }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deletePending">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="deletePending" @click="removeChallenge">
            <Spinner v-if="deletePending" data-icon="inline-start" />
            {{ deletePending ? $t('administration.label.processing') : $t('administration.label.confirmDeletion') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
