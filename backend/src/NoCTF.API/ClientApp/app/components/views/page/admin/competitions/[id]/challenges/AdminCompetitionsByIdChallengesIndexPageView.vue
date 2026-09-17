<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdChallengesIndexPageViewState } from '~/features/routes/admin/competitions/[id]/challenges/useAdminCompetitionsByIdChallengesIndexPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdChallengesIndexPageViewState }>()
const { Plus, competitionId, competition, canWrite, items, loading, error, includeDeleted, pendingId, addOpen, templatesLoading, selectedTemplateId, newCustomTitle, newOrder, adding, addError, modeTemplates, openAdd, addChallenge, deleteTarget, deletePending, deleteError, closeDeleteDialog, beginDeleteChallenge, removeChallenge, restoreChallenge, setChallengePublished, onClickAddOpen } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Checkbox id="show-deleted" v-model="includeDeleted" />
        <Label for="show-deleted" class="text-sm text-muted-foreground">{{ $t('ui.showDeleted') }}</Label>
      </div>
      <Button v-if="canWrite" size="sm" @click="openAdd">
        <Plus data-icon="inline-start" /> {{ $t('ui.addFromQuestionBank') }} </Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-48 w-full" />

    <Empty v-else-if="items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noTitleYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.startConfiguringTheCompetitionAfterAddingQuestionsFromTheQuestion') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead class="w-16">{{ $t('ui.order') }}</TableHead>
          <TableHead>{{ $t('ui.title') }}</TableHead>
          <TableHead>{{ $t('ui.category') }}</TableHead>
          <TableHead class="w-28">{{ $t('ui.status') }}</TableHead>
          <TableHead v-if="canWrite" class="w-40 text-right">{{ $t('ui.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="c in items" :key="c.id" :class="{ 'opacity-60': c.deletedAt }">
          <TableCell class="font-mono tabular-nums">{{ c.order }}</TableCell>
          <TableCell>
            <NuxtLink
              v-if="!c.deletedAt"
              class="font-medium underline-offset-4 hover:underline"
              :to="`/admin/competitions/${competitionId}/challenges/${c.id}`"
            >
              {{ c.title }}
            </NuxtLink>
            <span v-else class="font-medium">{{ c.title }}</span>
          </TableCell>
          <TableCell>
            <Badge variant="outline" :class="directionBadgeClass(c.direction)">
              {{ directionLabel(c.direction) }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge v-if="c.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
            <div v-else-if="canWrite" class="flex items-center gap-2">
              <Spinner v-if="pendingId === c.id" class="size-4" />
              <Switch
                v-else
                :id="`challenge-published-${c.id}`"
                :model-value="c.isPublished"
                :disabled="pendingId !== null"
                :aria-label="c.isPublished ? $t('ui.unpublishCompetitionChallenge') : $t('ui.publishCompetitionChallenge')"
                @update:model-value="setChallengePublished(c, $event)"
              />
              <FieldLabel :for="`challenge-published-${c.id}`" class="whitespace-nowrap text-xs">
                {{ c.isPublished ? $t('ui.published') : $t('ui.unpublished') }}
              </FieldLabel>
            </div>
            <Badge v-else :variant="c.isPublished ? 'default' : 'outline'">
              {{ c.isPublished ? $t('ui.published') : $t('ui.unpublished') }}
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
                <Spinner v-if="pendingId === c.id" data-icon="inline-start" /> {{ $t('ui.restore2') }} </Button>
              <Button
                v-else
                variant="ghost"
                size="sm"
                :disabled="pendingId !== null"
                @click="beginDeleteChallenge(c)"
              > {{ $t('ui.delete') }} </Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <Dialog v-model:open="addOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.addQuestionsFromQuestionBank') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.selectTheQuestionBankTemplateThatMatchesThisCompetitionMode') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="addError" variant="destructive">
            <AlertDescription>{{ $message(addError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="tpl">{{ $t('ui.questionBankTemplate') }}</FieldLabel>
            <Skeleton v-if="templatesLoading" class="h-9 w-full" />
            <Select v-else id="tpl" v-model="selectedTemplateId">
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="$t('ui.selectTemplate')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="t in modeTemplates" :key="t.id" :value="t.id!">
                    {{ t.title }}({{ directionLabel(t.direction) }})
                  </SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <FieldDescription v-if="!templatesLoading && modeTemplates.length === 0">
              {{ $t('ui.noAvailableTemplatesInTheChallengeLibrary', { mode: enumLabel(GameModeLabel, competition?.mode) }) }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="new-title">{{ $t('ui.competitionChallengeName') }}</FieldLabel>
            <Input
              id="new-title"
              v-model="newCustomTitle"
              maxlength="160"
              :placeholder="$t('ui.leaveBlankToUseTheQuestionBankTemplateTitle')"
            />
            <FieldDescription>{{ $t('ui.changesTheDisplayNameForThisCompetitionOnlyAndDoes') }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="new-order">{{ $t('ui.order') }}</FieldLabel>
            <NumberInput id="new-order" v-model.number="newOrder"  min="0" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickAddOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="adding || !selectedTemplateId" @click="addChallenge">
            <Spinner v-if="adding" data-icon="inline-start" /> {{ $t('ui.add') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteTarget !== null" @update:open="closeDeleteDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.deleteQuestion') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.participantsWillNoLongerSeeYouCanRestoreItLater', { title: deleteTarget?.title ?? '-' }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="deleteError" variant="destructive">
          <AlertDescription>{{ $message(deleteError) }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deletePending">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="deletePending" @click="removeChallenge">
            <Spinner v-if="deletePending" data-icon="inline-start" />
            {{ deletePending ? $t('ui.processing') : $t('ui.confirmDeletion') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
