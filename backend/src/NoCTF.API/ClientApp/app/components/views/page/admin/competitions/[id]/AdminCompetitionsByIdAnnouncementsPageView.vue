<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdAnnouncementsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdAnnouncementsPage'
const viewProps = defineProps<{ state: AdminCompetitionsByIdAnnouncementsPageViewState }>()
const { canJudge, includeWithdrawn, editingId, title, body, audience, saving, formError, valid, deleting, deleteTarget, deleteError, previewTarget, resetEditor, edit, save, requestDelete, setDeleteOpen, remove, preview, setPreviewOpen, refresh, audienceKey, adminUserPath, adminFormatDateTime, items, loading, error, initialized, page, pageCount, total, pageLimit, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-display text-xl">{{ $t('announcements.title') }}</h2>
      <Button variant="outline" size="sm" :disabled="loading" @click="refresh"><Spinner v-if="loading" data-icon="inline-start" />{{ $t('common.label.refresh') }}</Button>
    </div>
    <Card class="gap-0">
      <template v-if="canJudge">
        <CardHeader id="competition-announcement-editor"><CardTitle>{{ editingId ? $t('announcements.edit') : $t('administration.label.publishCompetitionNotice') }}</CardTitle></CardHeader>
        <CardContent>
          <UiForm validation="feature" @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="competition-announcement-audience">{{ $t('administration.label.notificationObject') }}</FieldLabel>
                <Select v-model="audience" :disabled="saving || deleting || editingId !== null">
                  <SelectTrigger id="competition-announcement-audience"><SelectValue /></SelectTrigger>
                  <SelectContent><SelectGroup><SelectItem value="Participants">{{ $t('administration.label.contestants') }}</SelectItem><SelectItem value="Collaborators">{{ $t('common.label.eventStaff') }}</SelectItem></SelectGroup></SelectContent>
                </Select>
                <FieldDescription v-if="editingId">{{ $t('announcements.audienceFixed') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="competition-announcement-title">{{ $t('common.label.title') }}</FieldLabel>
                <Input id="competition-announcement-title" v-model="title" maxlength="160" :disabled="saving || deleting" />
              </Field>
              <Field>
                <FieldLabel for="competition-announcement-body">{{ $t('common.label.content') }}</FieldLabel>
                <Textarea id="competition-announcement-body" v-model="body" rows="6" maxlength="16000" :disabled="saving || deleting" />
              </Field>
              <Alert v-if="formError" variant="destructive"><AlertDescription>{{ $message(formError) }}</AlertDescription></Alert>
              <Field orientation="horizontal">
                <Button type="submit" :disabled="saving || deleting || !valid"><Spinner v-if="saving" data-icon="inline-start" />{{ editingId ? $t('common.action.save') : $t('administration.label.postNotice') }}</Button>
                <Button v-if="editingId" type="button" variant="ghost" :disabled="saving || deleting" @click="resetEditor">{{ $t('common.action.cancel') }}</Button>
              </Field>
            </FieldGroup>
          </UiForm>
        </CardContent>
        <Separator class="my-6" />
      </template>
      <CardHeader class="flex flex-row flex-wrap items-center justify-between gap-3">
        <CardTitle>{{ $t('announcements.history') }}</CardTitle>
        <Field orientation="horizontal"><Switch v-model="includeWithdrawn" :aria-label="$t('announcements.includeWithdrawn')" /><span class="text-sm">{{ $t('announcements.includeWithdrawn') }}</span></Field>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
        <Skeleton v-if="loading && !initialized" class="h-40 w-full" />
        <Empty v-else-if="initialized && !items.length && !error" class="py-10"><EmptyHeader><EmptyTitle>{{ $t('announcements.empty') }}</EmptyTitle></EmptyHeader></Empty>
        <Table v-else-if="items.length">
          <TableHeader><TableRow><TableHead>{{ $t('common.label.title') }}</TableHead><TableHead>{{ $t('administration.label.notificationObject') }}</TableHead><TableHead>{{ $t('announcements.author') }}</TableHead><TableHead>{{ $t('announcements.publishedAt') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead><TableHead class="text-right">{{ $t('common.label.actions') }}</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="item in items" :key="item.id ?? undefined">
              <TableCell class="max-w-64 whitespace-normal break-words font-medium">{{ item.title }}</TableCell>
              <TableCell>{{ $t(audienceKey(item.audience)) }}</TableCell>
              <TableCell><NuxtLink v-if="item.authorId" :to="adminUserPath(item.authorId)" class="hover:underline">{{ item.authorName ?? item.authorId }}</NuxtLink><span v-else>{{ $t('common.label.system') }}</span></TableCell>
              <TableCell class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(item.publishedAt) }}</TableCell>
              <TableCell><Badge :variant="item.state === 'Withdrawn' ? 'outline' : 'default'">{{ item.state === 'Withdrawn' ? $t('announcements.withdrawn') : $t('administration.label.published') }}</Badge></TableCell>
              <TableCell><div class="flex flex-wrap justify-end gap-1"><Button variant="ghost" size="sm" @click="preview(item)">{{ $t('common.label.details') }}</Button><template v-if="canJudge && item.state === 'Published'"><Button variant="ghost" size="sm" :disabled="saving || deleting" @click="edit(item)">{{ $t('administration.label.edit') }}</Button><Button variant="destructive" size="sm" :disabled="saving || deleting" @click="requestDelete(item)">{{ $t('common.action.delete') }}</Button></template></div></TableCell>
            </TableRow>
          </TableBody>
        </Table>
        <OffsetPagination v-if="initialized" :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading" @update:page="loadPage" @update:limit="setPageSize" />
      </CardContent>
    </Card>
    <Sheet :open="previewTarget !== null" @update:open="setPreviewOpen">
      <SheetContent data-scroll-surface class="overflow-y-auto sm:max-w-xl">
        <SheetHeader><SheetTitle>{{ previewTarget?.title }}</SheetTitle><SheetDescription>{{ $t(audienceKey(previewTarget?.audience)) }}</SheetDescription></SheetHeader>
        <div v-if="previewTarget" class="flex flex-col gap-4 px-4 pb-4"><MarkdownContent :source="previewTarget.body ?? ''" /><Separator /><p class="text-xs text-muted-foreground">{{ $t('announcements.updatedAt') }}: {{ adminFormatDateTime(previewTarget.updatedAt) }}</p><Badge v-if="previewTarget.state === 'Withdrawn'" variant="outline">{{ $t('announcements.withdrawn') }}</Badge></div>
      </SheetContent>
    </Sheet>
    <AlertDialog :open="deleteTarget !== null" @update:open="setDeleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader><AlertDialogTitle>{{ $t('announcements.deleteTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('announcements.deleteDescription', { title: deleteTarget?.title ?? '' }) }}</AlertDialogDescription></AlertDialogHeader>
        <Alert v-if="deleteError" variant="destructive"><AlertDescription>{{ $message(deleteError) }}</AlertDescription></Alert>
        <AlertDialogFooter><AlertDialogCancel :disabled="deleting">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button variant="destructive" :disabled="deleting" @click="remove"><Spinner v-if="deleting" data-icon="inline-start" />{{ $t('common.action.delete') }}</Button></AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
