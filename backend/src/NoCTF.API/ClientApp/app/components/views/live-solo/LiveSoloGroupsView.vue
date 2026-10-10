<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloGroupsState } from '~/features/live-solo/useLiveSoloGroups'
const props = defineProps<{ state: LiveSoloGroupsState }>()
const { options, groupId, draft, loading, busy, error, saved, dirty, writable, questionOptions, questions, selectedQuestion, interval, limit, reload,
  select, create, add, remove, move, offset, setLimit, reserve, save, back, leaveOpen, setLeave, leave,
  keyword, copyId, copyAttachments, copyFlags, copyDialog, setCopyDialog, copyName, copied, templateOptions, templatePage, templatePageCount,
  templateLimit, templateTotal, templateLoading, templateError, searchTemplates, templatePageChange, templateLimitChange, openCopy, confirmCopy } = toRefs(props.state)
</script>
<template>
  <div class="mx-auto flex w-full max-w-7xl min-w-0 flex-col gap-6 px-4 pb-8 pt-4 md:px-8">
    <div><Button variant="ghost" size="sm" @click="back"><LucideIcon name="arrow-left" data-icon="inline-start" />{{ $t('liveSolo.back') }}</Button></div>
    <header class="flex flex-wrap items-center justify-between gap-4">
      <h1 class="text-display text-2xl">{{ $t('liveSolo.groups.title') }}</h1>
      <div class="flex flex-wrap gap-3"><Button variant="outline" :disabled="busy" @click="reload">{{ $t('common.label.refresh') }}</Button><Button v-if="writable" :disabled="busy" @click="create"><LucideIcon name="plus" data-icon="inline-start" />{{ $t('liveSolo.groups.new') }}</Button></div>
    </header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert><Alert v-if="saved"><AlertDescription>{{ $t('liveSolo.groups.saved') }}</AlertDescription></Alert>
    <div class="grid min-w-0 items-start gap-6 xl:grid-cols-[15rem_minmax(0,1fr)]">
      <div class="min-w-0 xl:hidden"><ChoicePicker :items="options" :model-value="groupId" :label="$t('liveSolo.groups.title')" :search-label="$t('liveSolo.groups.name')" :empty-label="$t('liveSolo.groups.empty')" :disabled="busy" @update:model-value="select" /></div>
      <div class="hidden h-[min(32rem,65dvh)] min-w-0 xl:sticky xl:top-4 xl:block"><ChoiceSidebar contained :items="options" :model-value="groupId" :loading="loading" :label="$t('liveSolo.groups.title')" :loading-label="$t('liveSolo.groups.title')" :empty-label="$t('liveSolo.groups.empty')" @update:model-value="select"><template #item="{item}"><span class="flex min-w-0 flex-col gap-1"><span class="truncate font-semibold">{{ item.label }}</span><span class="text-xs text-muted-foreground">{{ $t(item.row.reserve ? 'liveSolo.groups.reserve' : 'liveSolo.groups.regular') }} · {{ item.row.questions?.length ?? 0 }}</span></span></template></ChoiceSidebar></div>
      <Card class="min-w-0"><CardContent class="flex min-w-0 flex-col gap-6">
        <Tabs default-value="group" class="min-w-0"><TabsList class="h-auto flex-wrap"><TabsTrigger value="group">{{ $t('liveSolo.groups.edit') }}</TabsTrigger><TabsTrigger v-if="writable" value="copy">{{ $t('liveSolo.groups.copyTitle') }}</TabsTrigger></TabsList>
          <TabsContent value="group" class="pt-4"><UiForm validation="feature" class="flex min-w-0 flex-col gap-6" @submit="save">
            <section class="flex flex-col gap-4"><h2 class="text-display">{{ $t('liveSolo.groups.details') }}</h2><FieldGroup class="grid min-w-0 gap-5 md:grid-cols-2"><Field><FieldLabel for="group-name">{{ $t('liveSolo.groups.name') }}</FieldLabel><Input id="group-name" v-model="draft.name" :disabled="!writable || busy" :maxlength="160" /></Field><Field><FieldLabel for="group-limit">{{ $t('liveSolo.groups.limit') }}</FieldLabel><NumberInput id="group-limit" :model-value="draft.limitSeconds" :min="1" :max="86400" :disabled="!writable || busy" @update:model-value="setLimit" /><FieldDescription>{{ $t('liveSolo.groups.defaultLimit',{seconds:limit}) }}</FieldDescription></Field><Field orientation="horizontal"><Switch id="group-reserve" :model-value="draft.reserve" :disabled="!writable || busy" @update:model-value="reserve" /><FieldLabel for="group-reserve">{{ $t('liveSolo.groups.reserve') }}</FieldLabel></Field></FieldGroup></section>
            <Separator />
            <section class="flex min-w-0 flex-col gap-4"><h2 class="text-display">{{ $t('liveSolo.groups.schedule') }}</h2>
            <p class="text-sm text-muted-foreground">{{ $t('liveSolo.groups.scheduleHelp',{seconds:interval}) }}</p>
            <div v-if="writable" class="flex min-w-0 flex-col gap-3 sm:flex-row">
              <div class="min-w-0 flex-1"><ChoicePicker v-model="selectedQuestion" :items="questionOptions" :label="$t('liveSolo.groups.selectQuestion')" :search-label="$t('liveSolo.groups.searchQuestion')" :empty-label="$t('liveSolo.groups.noQuestion')" :disabled="busy" /></div>
              <Button type="button" class="shrink-0" :disabled="busy || !selectedQuestion" @click="add"><LucideIcon name="plus" data-icon="inline-start" />{{ $t('liveSolo.groups.addQuestion') }}</Button>
            </div>
            <div class="flex min-w-0 flex-col">
              <template v-for="row in questions" :key="row.competitionChallengeId">
                <Separator v-if="row.index > 0" />
                <article class="flex min-w-0 flex-col gap-4 py-4">
                  <div class="min-w-0"><h3 class="break-words font-semibold">{{ row.index + 1 }} · {{ row.title }}</h3><p class="text-sm text-muted-foreground">{{ $t('liveSolo.groups.effectiveOffset', { seconds: row.effectiveOffset }) }}</p></div>
                  <div class="flex flex-wrap items-end justify-between gap-4">
                    <Field class="w-44"><FieldLabel :for="'question-offset-' + row.index">{{ $t('liveSolo.groups.offset') }}</FieldLabel><NumberInput :id="'question-offset-' + row.index" :model-value="row.openOffsetSeconds" :min="0" :max="86400" :disabled="!writable || busy" @update:model-value="offset(row.index, $event)" /></Field>
                    <div v-if="writable" class="flex flex-wrap gap-2"><Button type="button" variant="ghost" :disabled="busy || row.index === 0" :aria-label="$t('liveSolo.groups.moveUp', { question: row.title })" @click="move(row.index, -1)">{{ $t('liveSolo.bracket.upShort') }}</Button><Button type="button" variant="ghost" :disabled="busy || row.index === questions.length - 1" :aria-label="$t('liveSolo.groups.moveDown', { question: row.title })" @click="move(row.index, 1)">{{ $t('liveSolo.bracket.downShort') }}</Button><Button type="button" variant="outline" :disabled="busy" @click="remove(row.index)">{{ $t('liveSolo.settings.remove') }}</Button></div>
                  </div>
                </article>
              </template>
            </div>
            <Empty v-if="!questions.length"><EmptyHeader><EmptyTitle>{{ $t('liveSolo.groups.noQuestion') }}</EmptyTitle></EmptyHeader></Empty></section><Separator /><div v-if="writable" class="flex flex-wrap items-center justify-end gap-3"><Badge v-if="dirty" variant="secondary">{{ $t('liveSolo.settings.unsaved') }}</Badge><Button type="submit" :disabled="busy || !dirty"><Spinner v-if="busy" data-icon="inline-start" />{{ $t('liveSolo.groups.save') }}</Button></div>
          </UiForm></TabsContent>
          <TabsContent v-if="writable" value="copy"><div class="flex flex-col gap-4"><Alert v-if="copied"><AlertDescription>{{ $t('liveSolo.groups.copySucceeded',{title:copied.title || '—',attachments:copied.attachmentCount ?? 0,flags:copied.flagCount ?? 0}) }}</AlertDescription></Alert><p class="text-sm text-muted-foreground">{{ $t('liveSolo.groups.copyHelp') }}</p><div class="flex min-w-0 flex-col gap-3 sm:flex-row"><Input class="min-w-0 flex-1" v-model="keyword" :aria-label="$t('liveSolo.groups.searchTemplate')" :placeholder="$t('liveSolo.groups.searchTemplate')" @keydown.enter="searchTemplates" /><Button class="shrink-0" :disabled="templateLoading || busy" @click="searchTemplates">{{ $t('liveSolo.groups.findTemplates') }}</Button></div><Alert v-if="templateError" variant="destructive"><AlertDescription>{{ $message(templateError.displayMessage) }}</AlertDescription></Alert><ChoicePicker v-model="copyId" :items="templateOptions" :label="$t('liveSolo.groups.selectTemplate')" :search-label="$t('liveSolo.groups.searchTemplate')" :empty-label="$t('liveSolo.groups.noTemplate')" :disabled="busy || templateLoading" /><OffsetPagination :page="templatePage" :page-count="templatePageCount" :total="templateTotal" :limit="templateLimit" :loading="templateLoading" @update:page="templatePageChange" @update:limit="templateLimitChange" /><Field orientation="horizontal"><Checkbox id="copy-attachments" v-model="copyAttachments" :disabled="busy" /><FieldLabel for="copy-attachments">{{ $t('liveSolo.groups.copyAttachments') }}</FieldLabel></Field><Field orientation="horizontal"><Checkbox id="copy-flags" v-model="copyFlags" :disabled="busy" /><FieldLabel for="copy-flags">{{ $t('liveSolo.groups.copyFlags') }}</FieldLabel></Field><p class="text-sm text-muted-foreground">{{ $t('liveSolo.groups.flagsPermission') }}</p><div><Button :disabled="busy || !copyId" @click="openCopy">{{ $t('liveSolo.groups.copy') }}</Button></div></div></TabsContent>
        </Tabs>
      </CardContent></Card>
    </div>
    <AlertDialog :open="copyDialog" @update:open="setCopyDialog"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('liveSolo.groups.copyConfirm') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.groups.copyHelp') }}</AlertDialogDescription></AlertDialogHeader><p class="break-words font-semibold">{{ copyName }}</p><div class="flex flex-wrap gap-2"><Badge variant="secondary">{{ $t(copyAttachments ? 'liveSolo.groups.copyAttachments' : 'liveSolo.groups.copyAttachmentsSkipped') }}</Badge><Badge variant="secondary">{{ $t(copyFlags ? 'liveSolo.groups.copyFlags' : 'liveSolo.groups.copyFlagsSkipped') }}</Badge></div><AlertDialogFooter><AlertDialogCancel :disabled="busy">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="busy" @click="confirmCopy">{{ $t('liveSolo.groups.copy') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
    <AlertDialog :open="leaveOpen" @update:open="setLeave"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('liveSolo.groups.discardTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.groups.discardDescription') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel><Button @click="leave">{{ $t('liveSolo.groups.discard') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </div>
</template>
