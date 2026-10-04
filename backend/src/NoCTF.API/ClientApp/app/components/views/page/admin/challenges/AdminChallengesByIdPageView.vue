<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminChallengesByIdPageViewState } from '~/features/routes/admin/challenges/useAdminChallengesByIdPage'

const viewProps = defineProps<{ state: AdminChallengesByIdPageViewState }>()
const { Paperclip, RotateCcw, Trash2, Upload, challengeId, selectedSection, canOrganize, template, loading, loadError, form, saving, basicSaveErrors, runtimeSaveErrors, definitionSaveErrors, deleting, restoring, isDeleted, titleInvalid, directionInvalid, definitionModel, definitionParseFailed, runtimeDefinitionModel, runtimeDefinitionParseFailed, hasModeDefinition, usesRuntimeFlagInjection, runtimeDisabled, runtimeDefinitionDirty, CtfInteraction, showInteractionKind, setInteractionKind, changeMode, resetRuntimeDefinition, resetModeDefinition, saveBasic, saveRuntimeDefinition, saveModeDefinition, removeTemplate, restoreTemplate, attachments, attachmentsLoading, attachmentsIncludeDeleted, attachmentDeliveryPolicy, uploading, uploadInput, randomBatchOpen, randomUploading, randomDownloadFileName, randomFiles, randomUploadInput, deletingAttachment, attachmentActionPending, requestAttachmentDeliveryPolicy, uploadAttachment, selectRandomFiles, uploadRandomBatch, confirmDeleteAttachment, restoreAttachment, formatBytes, flags, supportsRegularExpression, flagsLoading, flagsIncludeDeleted, flagCreating, flagForm, deletingFlag, flagActionPending, staticFlags, systemFlags, createFlag, confirmDeleteFlag, restoreFlag, managersText, permissionsSaving, newOwnerId, transferOpen, transferring, savePermissions, transferOwner, AdminDateTime, AdminGameModeBadge, ChallengeTestRuntimePanel, ChallengeCompetitionPlacements, DefinitionCheckerSection, DefinitionFlagInjectionSection, DefinitionFlagTemplateSection, DefinitionPatchSection, DefinitionRuntimeSection, setUploadInputRef, setRandomUploadInputRef, onBlurFormDirection, onClickRandomBatchOpen, onClickDeletingAttachment, onClickDeletingFlag, onClickTransferOpen, onUpdateOpenDeletingAttachment, onClickRandomBatchOpen2, onUpdateOpenDeletingFlag } = toRefs(viewProps.state)
</script>

<template>
  <div data-contained-workspace-page data-slot="challenge-template-workspace" class="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-5">
    <div class="flex items-center gap-2 text-sm text-muted-foreground">
      <NuxtLink to="/admin/challenges" class="hover:underline">{{ $t('common.label.challengeLibrary.useDefaultLayout') }}</NuxtLink>
      <span>/</span>
      <span class="min-w-0 truncate">{{ template?.title ?? challengeId }}</span>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('administration.challengesBy.validation.organizerAdministratorRequired') }}</AlertDescription>
    </Alert>

    <template v-else>
      <Alert v-if="loadError" variant="destructive">
        <AlertDescription>{{ $message(loadError) }}</AlertDescription>
      </Alert>

      <div v-if="loading" class="flex flex-col gap-3">
        <Skeleton class="h-10 w-1/3" />
        <Skeleton class="h-64 w-full" />
      </div>

      <template v-else-if="template">
        <div class="flex flex-wrap items-center justify-between gap-4">
          <div class="flex min-w-0 items-center gap-3">
            <h1 class="truncate text-2xl font-semibold">{{ template.title }}</h1>
            <component :is="AdminGameModeBadge" :mode="template.mode" />
            <Badge v-if="isDeleted" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <Button v-if="isDeleted" variant="outline" :disabled="restoring" @click="restoreTemplate">
              <Spinner v-if="restoring" data-icon="inline-start" />
              <RotateCcw v-else data-icon="inline-start" /> {{ $t('administration.label.recoveryTemplate') }} </Button>
            <AlertDialog v-else>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">
                  <Trash2 data-icon="inline-start" /> {{ $t('administration.label.deleteTemplate') }} </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ $t('administration.label.deleteQuestionTemplate') }}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {{ $t('administration.challengesBy.description.softDeleteTemplateRestored', { title: template.title ?? $t('common.label.symbol') }) }}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" :disabled="deleting" @click="removeTemplate">
                    <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('administration.label.confirmDeletion') }} </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>

        <Tabs
          v-model="selectedSection"
          orientation="vertical"
          class="grid min-h-0 flex-1 grid-rows-[auto_minmax(0,1fr)] gap-4 lg:grid-cols-[12rem_minmax(0,1fr)] lg:grid-rows-[minmax(0,1fr)] lg:gap-6"
        >
          <ScrollSurface as="aside" axis="y" class="min-h-0 overflow-y-auto lg:pr-2" :aria-label="$t('common.label.challengeLibrary.useDefaultLayout')">
            <TabsList class="grid h-auto w-full grid-cols-2 items-stretch gap-1 bg-transparent p-0 sm:grid-cols-3 lg:flex lg:flex-col">
              <TabsTrigger value="basic" class="w-full justify-start px-3 py-2">{{ $t('administration.label.basicInformation') }}</TabsTrigger>
              <TabsTrigger value="runtime" class="w-full justify-start px-3 py-2">{{ $t('common.label.runtimeEnvironment') }}</TabsTrigger>
              <TabsTrigger value="definition" class="w-full justify-start px-3 py-2">{{ $t('administration.label.modeDefinition') }}</TabsTrigger>
              <TabsTrigger value="attachments" class="w-full justify-start px-3 py-2">
                {{ $t('common.label.accessories') }}
                <Badge variant="secondary" class="ml-auto">{{ attachments.length }}</Badge>
              </TabsTrigger>
              <TabsTrigger value="flags" class="w-full justify-start px-3 py-2">
                {{ $t('administration.label.flags') }}
                <Badge variant="secondary" class="ml-auto">{{ flags.length }}</Badge>
              </TabsTrigger>
              <TabsTrigger value="permissions" class="w-full justify-start px-3 py-2">{{ $t('administration.label.permissions') }}</TabsTrigger>
              <TabsTrigger value="placements" class="w-full justify-start px-3 py-2">{{ $t('placements.title') }}</TabsTrigger>
            </TabsList>
          </ScrollSurface>

          <div data-slot="challenge-template-panels" class="min-h-0 min-w-0">
          <TabsContent value="placements" class="mt-0">
            <component :is="ChallengeCompetitionPlacements" :challenge-id="challengeId" :mode="template.mode ?? 'Ctf'" :disabled="isDeleted" />
          </TabsContent>
          <TabsContent value="basic" class="mt-0">
            <Card>
              <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                <Alert v-if="basicSaveErrors.length" variant="destructive" class="mb-4">
                  <AlertTitle>{{ $t('administration.challengesBy.description.unableSaveChallengeTemplate') }}</AlertTitle>
                  <AlertDescription>
                    <ul class="list-disc pl-5">
                      <li v-for="(message, index) in basicSaveErrors" :key="index">{{ message }}</li>
                    </ul>
                  </AlertDescription>
                </Alert>
                <UiForm validation="feature" @submit.prevent="saveBasic">
                  <FieldGroup>
                    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)]">
                    <Field :data-invalid="titleInvalid || undefined">
                      <FieldLabel for="edit-title">{{ $t('common.label.title') }}</FieldLabel>
                      <Input
                        id="edit-title"
                        v-model="form.title"
                        required
                        maxlength="160"
                        :aria-invalid="titleInvalid || undefined"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <Field>
                      <FieldLabel for="edit-visibility">{{ $t('administration.label.visibility') }}</FieldLabel>
                      <Select v-model="form.visibility" :disabled="isDeleted">
                        <SelectTrigger id="edit-visibility" class="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem value="Private">{{ $t('administration.label.private') }}</SelectItem>
                            <SelectItem value="Shared">{{ $t('common.label.share') }}</SelectItem>
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field :data-invalid="directionInvalid || undefined">
                      <FieldLabel for="edit-direction">{{ $t('administration.label.category') }}</FieldLabel>
                      <Input
                        id="edit-direction"
                        v-model="form.direction"
                        @blur="onBlurFormDirection"
                        required
                        maxlength="96"
                        :aria-invalid="directionInvalid || undefined"
                        :disabled="isDeleted"
                      />
                    </Field>
                    </div>
                    <div class="grid min-w-0 gap-4 xl:grid-cols-2">
                      <Field>
                        <FieldLabel for="edit-description">{{ $t('administration.label.question') }}</FieldLabel>
                        <Textarea id="edit-description" v-model="form.description" rows="8" :disabled="isDeleted" />
                      </Field>
                      <MarkdownPreview :source="form.description" :label="$t('administration.label.markdownPreview')" :empty-label="$t('administration.label.content')" />
                    </div>
                    <div class="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                      <span>{{ $t('common.action.create') }} <component :is="AdminDateTime" :value="template.createdAt" /></span>
                      <span>{{ $t('administration.label.update') }} <component :is="AdminDateTime" :value="template.updatedAt" /></span>
                      <span v-if="template.deletedAt">{{ $t('common.action.delete') }} <component :is="AdminDateTime" :value="template.deletedAt" /></span>
                    </div>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button type="submit" :disabled="saving">
                        <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveChanges') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </CardContent>
              </ScrollSurface>
            </Card>
          </TabsContent>

          <TabsContent value="runtime" class="mt-0">
            <Card>
              <CardHeader v-if="!isDeleted" class="shrink-0">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetRuntimeDefinition">
                      <RotateCcw data-icon="inline-start" /> {{ $t('administration.challengesBy.label.resetModeDefinition') }}
                    </Button>
                    <Button data-testid="runtime-definition-save" type="button" size="sm" :disabled="saving" @click="saveRuntimeDefinition">
                      <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveChanges') }}
                    </Button>
                  </div>
              </CardHeader>
              <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                <Alert v-if="runtimeSaveErrors.length" variant="destructive" class="mb-4">
                  <AlertTitle>{{ $t('administration.challengesBy.description.unableSaveChallengeTemplate') }}</AlertTitle>
                  <AlertDescription>
                    <ul class="list-disc pl-5">
                      <li v-for="(message, index) in runtimeSaveErrors" :key="index">{{ message }}</li>
                    </ul>
                  </AlertDescription>
                </Alert>
                <Alert v-if="runtimeDefinitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('administration.challengesBy.validation.existingDefinitionFormat') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetRuntimeDefinition">
                      <RotateCcw data-icon="inline-start" /> {{ $t('administration.challengesBy.label.resetModeDefinition') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="runtimeDefinitionModel">
                  <component :is="DefinitionRuntimeSection" :model="runtimeDefinitionModel" :mode="template.mode ?? 'Ctf'" :disabled="isDeleted" />
                  <component :is="ChallengeTestRuntimePanel"
                    v-if="runtimeDefinitionModel.runtime && !isDeleted"
                    :challenge-id="challengeId"
                    :definition-dirty="runtimeDefinitionDirty"
                  />
                </FieldGroup>
              </CardContent>
              </ScrollSurface>
            </Card>
          </TabsContent>

          <TabsContent value="definition" class="mt-0">
            <Card>
              <CardHeader v-if="!isDeleted" class="shrink-0">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetModeDefinition">
                      <RotateCcw data-icon="inline-start" /> {{ $t('administration.challengesBy.label.resetModeDefinition') }}
                    </Button>
                    <Button data-testid="mode-definition-save" type="button" size="sm" :disabled="saving" @click="saveModeDefinition">
                      <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveChanges') }}
                    </Button>
                  </div>
              </CardHeader>
              <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                <Alert v-if="definitionSaveErrors.length" variant="destructive" class="mb-4">
                  <AlertTitle>{{ $t('administration.challengesBy.description.unableSaveChallengeTemplate') }}</AlertTitle>
                  <AlertDescription>
                    <ul class="list-disc pl-5">
                      <li v-for="(message, index) in definitionSaveErrors" :key="index">{{ message }}</li>
                    </ul>
                  </AlertDescription>
                </Alert>
                <Alert v-if="definitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('administration.challengesBy.validation.existingDefinitionFormat.idPageView') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetModeDefinition">
                      <RotateCcw data-icon="inline-start" /> {{ $t('administration.challengesBy.label.resetModeDefinition') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="definitionModel">
                  <Alert v-if="hasModeDefinition && runtimeDisabled">
                    <AlertDescription>{{ $t('administration.challengesBy.description.followingConfigurationsTakeEffect') }}</AlertDescription>
                  </Alert>
                  <Field>
                    <FieldLabel for="edit-mode">{{ $t('common.label.gameMode') }}</FieldLabel>
                    <Select :model-value="form.mode" :disabled="isDeleted" @update:model-value="changeMode">
                      <SelectTrigger id="edit-mode" class="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="Ctf">{{ $t('common.label.ctf') }}</SelectItem>
                          <SelectItem value="Awd">{{ $t('common.label.awd') }}</SelectItem>
                          <SelectItem value="Awdp">{{ $t('common.label.awdp.createDialogView') }}</SelectItem>
                          <SelectItem value="Koh">{{ $t('common.label.koh') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                    <FieldDescription>{{ $t('administration.challengesBy.validation.modeModifiedFormat') }}</FieldDescription>
                  </Field>
                  <Field v-if="showInteractionKind">
                    <FieldLabel for="challenge-ctf-interaction-kind">{{ $t('administration.label.completionMethod') }}</FieldLabel>
                    <Select
                      :model-value="definitionModel.interactionKind === CtfInteraction.PatchVerification ? 'PatchVerification' : 'FlagSubmission'"
                      :disabled="isDeleted"
                      @update:model-value="setInteractionKind"
                    >
                      <SelectTrigger id="challenge-ctf-interaction-kind" class="w-full"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="FlagSubmission">{{ $t('administration.label.flagSubmission') }}</SelectItem>
                          <SelectItem value="PatchVerification">{{ $t('common.label.patchVerification') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                    <FieldDescription>{{ $t('administration.label.ctfCompletionMethodDescription') }}</FieldDescription>
                  </Field>
                  <template v-if="form.mode === 'Awd'">
                    <component :is="DefinitionFlagInjectionSection" :model="definitionModel" :disabled="isDeleted" />
                    <component :is="DefinitionCheckerSection" :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                    <component :is="DefinitionFlagTemplateSection" :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  </template>
                  <template v-else-if="form.mode === 'Awdp'">
                    <component :is="DefinitionPatchSection" :model="definitionModel" :disabled="isDeleted" />
                    <component :is="DefinitionCheckerSection" :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  </template>
                  <template v-else-if="form.mode === 'Ctf'">
                    <template v-if="definitionModel.interactionKind === CtfInteraction.PatchVerification">
                      <component :is="DefinitionPatchSection" :model="definitionModel" :disabled="isDeleted" />
                      <component :is="DefinitionCheckerSection" :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                    </template>
                    <component v-else :is="DefinitionFlagTemplateSection"
                      :model="definitionModel"
                      :mode="form.mode"
                      :disabled="isDeleted"
                    />
                  </template>
                  <Empty v-if="!hasModeDefinition">
                    <EmptyHeader>
                      <EmptyTitle>{{ $t('administration.challengesBy.description.modeAdditionalModeDefinition') }}</EmptyTitle>
                    </EmptyHeader>
                  </Empty>
                  <FieldDescription v-else> {{ $t('administration.definitionEditor.description.definitionModificationsTakeEffect') }} </FieldDescription>
                </FieldGroup>
              </CardContent>
              </ScrollSurface>
            </Card>
          </TabsContent>

          <TabsContent value="attachments" class="mt-0">
            <Card>
              <CardHeader class="gap-4">
                <div class="flex flex-wrap items-center justify-between gap-4">
                  <div>
                    <CardTitle>{{ $t('administration.label.attachmentDelivery') }}</CardTitle>
                    <CardDescription>
                      {{ attachmentDeliveryPolicy === 'RandomOnePerTeam'
                        ? $t('administration.challengesBy.description.teamReceivesOneStable')
                        : $t('administration.challengesBy.description.participantsViewDownloadEvery') }}
                    </CardDescription>
                  </div>
                  <Badge variant="outline">
                    {{ attachmentDeliveryPolicy === 'RandomOnePerTeam' ? $t('administration.challengesBy.label.oneRandomVariantTeam') : $t('administration.label.attachments') }}
                  </Badge>
                </div>
                <div class="flex flex-wrap items-end justify-between gap-3">
                  <div class="flex items-center gap-2">
                    <Switch id="attachments-include-deleted" v-model="attachmentsIncludeDeleted" />
                    <Label for="attachments-include-deleted">{{ $t('administration.label.showDeleted') }}</Label>
                  </div>
                  <div class="flex flex-wrap items-end gap-2">
                    <div class="grid min-w-48 gap-1.5">
                      <Label for="attachment-delivery-policy">{{ $t('administration.label.deliveryMode') }}</Label>
                      <Select
                        :model-value="attachmentDeliveryPolicy"
                        :disabled="isDeleted || uploading || randomUploading"
                        @update:model-value="requestAttachmentDeliveryPolicy"
                      >
                        <SelectTrigger id="attachment-delivery-policy" class="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="All">{{ $t('administration.label.attachments') }}</SelectItem>
                          <SelectItem value="RandomOnePerTeam">{{ $t('administration.challengesBy.label.oneRandomVariantTeam') }}</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <template v-if="attachmentDeliveryPolicy === 'All'">
                      <FileInput :ref="setUploadInputRef"  multiple class="hidden" @change="uploadAttachment" />
                      <Button :disabled="uploading || isDeleted" @click="uploadInput?.click()">
                        <Spinner v-if="uploading" data-icon="inline-start" />
                        <Upload v-else data-icon="inline-start" /> {{ $t('administration.label.uploadStandardAttachments') }}
                      </Button>
                      <Button
                        variant="outline"
                        :disabled="isDeleted || attachments.some(item => !item.deletedAt)"
                        @click="onClickRandomBatchOpen(true)"
                      >
                        {{ $t('administration.challengesBy.label.createTeamRandomAttachments') }}
                      </Button>
                    </template>
                    <Button v-else :disabled="isDeleted" @click="onClickRandomBatchOpen(true)">
                      {{ $t('administration.label.uploadRandomAttachmentBatch') }}
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                <div v-if="attachmentsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="attachments.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('administration.label.attachmentsYet') }}</EmptyTitle>
                    <EmptyDescription>{{ $t('administration.challengesBy.validation.uploadAttachmentRequired') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <Table v-else>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{{ $t('administration.label.fileName') }}</TableHead>
                      <TableHead v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'">{{ $t('administration.label.exactFlag') }}</TableHead>
                      <TableHead v-else>{{ $t('common.label.type') }}</TableHead>
                      <TableHead>{{ $t('administration.label.hash') }}</TableHead>
                      <TableHead>{{ $t('administration.label.size') }}</TableHead>
                      <TableHead>{{ $t('administration.label.uploadTime') }}</TableHead>
                      <TableHead>{{ $t('common.label.status') }}</TableHead>
                      <TableHead class="text-right">{{ $t('common.label.actions') }}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="attachment in attachments" :key="attachment.id">
                      <TableCell class="font-medium">
                        <span class="inline-flex items-center gap-2">
                          <Paperclip class="size-4 text-muted-foreground" />
                          {{ attachment.fileName }}
                        </span>
                      </TableCell>
                      <TableCell v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'" class="font-mono text-sm">
                        {{ attachment.exactFlag ?? $t('common.label.symbol') }}
                      </TableCell>
                      <TableCell v-else class="text-muted-foreground">{{ attachment.contentType }}</TableCell>
                      <Hint :content="attachment.sha256" ><TableCell tabindex="0"
                        class="font-mono text-xs tabular-nums text-muted-foreground"

                      >
                        {{ attachment.sha256?.slice(0, 8) ?? $t('common.label.symbol') }}
                      </TableCell></Hint>
                      <TableCell>{{ formatBytes(attachment.byteLength) }}</TableCell>
                      <TableCell>
                        <component :is="AdminDateTime" :value="attachment.createdAt" />
                      </TableCell>
                      <TableCell>
                        <Badge v-if="attachment.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('administration.label.normal') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <div class="flex justify-end gap-2">
                          <template v-if="attachment.deletedAt">
                            <Button
                              size="sm"
                              variant="outline"
                              :disabled="attachmentActionPending"
                              @click="restoreAttachment(attachment)"
                            > {{ $t('administration.label.restore') }} </Button>
                          </template>
                          <template v-else>
                            <Button size="sm" variant="destructive" @click="onClickDeletingAttachment(attachment)"> {{ $t('common.action.delete') }} </Button>
                          </template>
                        </div>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </CardContent>
              </ScrollSurface>
            </Card>
          </TabsContent>

          <TabsContent value="flags" class="mt-0">
            <Card>
              <CardHeader class="flex flex-row items-center justify-between gap-4">
                <div class="flex items-center gap-2">
                  <Switch id="flags-include-deleted" v-model="flagsIncludeDeleted" />
                  <Label for="flags-include-deleted">{{ $t('administration.label.showDeleted') }}</Label>
                </div>
              </CardHeader>
              <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                <section v-if="!usesRuntimeFlagInjection" class="mb-6 flex flex-col gap-4" aria-labelledby="add-static-flag-title">
                  <div>
                    <h3 id="add-static-flag-title" class="text-sm font-semibold">{{ $t('administration.label.addFlag') }}</h3>
                  </div>
                  <UiForm validation="feature" @submit.prevent="createFlag">
                    <div class="grid gap-4 lg:grid-cols-[minmax(12rem,16rem)_minmax(0,1fr)_auto] lg:items-end">
                      <Field>
                        <FieldLabel for="flag-match-kind">{{ $t('administration.label.matchType') }}</FieldLabel>
                        <Select v-model="flagForm.matchKind" :disabled="isDeleted || flagCreating">
                          <SelectTrigger id="flag-match-kind" class="w-full"><SelectValue /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="Exact">{{ $t('administration.label.exactMatch') }}</SelectItem>
                            <SelectItem v-if="supportsRegularExpression" value="RegularExpression">{{ $t('administration.label.regularExpression') }}</SelectItem>
                          </SelectContent>
                        </Select>
                      </Field>
                      <Field>
                        <FieldLabel for="flag-value">{{ flagForm.matchKind === 'RegularExpression' ? $t('administration.label.regularExpression.idPageView') : $t('administration.label.flagContent') }}</FieldLabel>
                        <Input id="flag-value" v-model="flagForm.flag" required class="font-mono text-sm" :disabled="isDeleted || flagCreating" :placeholder="flagForm.matchKind === 'RegularExpression' ? $t('administration.label.flagF') : $t('administration.label.flag.idPageView')" />
                      </Field>
                      <Button type="submit" :disabled="isDeleted || flagCreating || !flagForm.flag.trim()">
                        <Spinner v-if="flagCreating" data-icon="inline-start" />{{ $t('administration.label.add') }}
                      </Button>
                    </div>
                  </UiForm>
                </section>
                <Separator v-if="!usesRuntimeFlagInjection" class="mb-6" />
                <Alert v-if="usesRuntimeFlagInjection" class="mb-4">
                  <AlertDescription>
                    {{ $t('administration.challengesBy.description.challengeUsesRuntimeManaged') }}
                  </AlertDescription>
                </Alert>
                <div v-if="flagsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="staticFlags.length === 0 && systemFlags.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('administration.label.flagYet') }}</EmptyTitle>
                    <EmptyDescription v-if="!usesRuntimeFlagInjection">{{ $t('administration.challengesBy.label.addTemplateLevelStatic') }}</EmptyDescription>
                    <EmptyDescription v-else>{{ $t('administration.challengesBy.description.dynamicFlagGeneratedAutomatically') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <div v-else class="space-y-6">
                  <section v-if="staticFlags.length > 0" class="space-y-2">
                    <h3 class="text-sm font-semibold">{{ $t('administration.label.staticFlags') }}</h3>
                    <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{{ $t('common.label.flag.flagSubmitView') }}</TableHead>
                      <TableHead>{{ $t('common.label.status') }}</TableHead>
                      <TableHead class="text-right">{{ $t('common.label.actions') }}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="flag in staticFlags" :key="flag.id">
                      <Hint :content="flag.flag" ><TableCell tabindex="0" class="max-w-md" >
                        <div class="flex min-w-0 items-center gap-2">
                          <Badge variant="outline">
                            {{ flag.matchKind === 'RegularExpression' ? $t('administration.label.regularExpression') : $t('administration.label.exactMatch') }}
                          </Badge>
                          <span class="truncate font-mono text-sm">{{ flag.flag }}</span>
                        </div>
                      </TableCell></Hint>
                      <TableCell>
                        <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('administration.label.normal') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <Button
                          v-if="flag.deletedAt"
                          size="sm"
                          variant="outline"
                          :disabled="flagActionPending"
                          @click="restoreFlag(flag)"
                        > {{ $t('administration.label.restore') }} </Button>
                        <Button v-else size="sm" variant="destructive" @click="onClickDeletingFlag(flag)"> {{ $t('common.action.delete') }} </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                    </Table>
                  </section>

                  <section v-if="systemFlags.length > 0" class="space-y-2">
                    <div>
                      <h3 class="text-sm font-semibold">{{ $t('administration.label.systemGeneratedDynamicFlags') }}</h3>
                    </div>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>{{ $t('common.label.flag.flagSubmitView') }}</TableHead>
                          <TableHead>{{ $t('common.label.team') }}</TableHead>
                          <TableHead>{{ $t('administration.label.internalLink') }}</TableHead>
                          <TableHead>{{ $t('administration.label.validityPeriod') }}</TableHead>
                          <TableHead>{{ $t('common.label.status') }}</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        <TableRow v-for="flag in systemFlags" :key="flag.id">
                          <Hint :content="flag.flag" ><TableCell tabindex="0" class="max-w-md truncate font-mono text-sm" >{{ flag.flag }}</TableCell></Hint>
                          <TableCell class="font-mono text-xs">{{ flag.teamId ?? $t('common.label.symbol') }}</TableCell>
                          <TableCell class="font-mono text-xs">
                            {{ flag.specificationKind ?? $t('common.label.symbol') }}<span v-if="flag.specificationId"> · {{ flag.specificationId }}</span>
                          </TableCell>
                          <TableCell class="text-sm text-muted-foreground">
                            <template v-if="flag.validStart || flag.validUntil">
                              <component :is="AdminDateTime" :value="flag.validStart" /> {{ $t('administration.label.byId') }} <component :is="AdminDateTime" :value="flag.validUntil" />
                            </template>
                            <span v-else>{{ $t('administration.challengesBy.label.effectiveLongTime') }}</span>
                          </TableCell>
                          <TableCell>
                            <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('administration.label.expired') }}</Badge>
                            <Badge v-else variant="secondary">{{ $t('common.label.injected') }}</Badge>
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </section>
                </div>
              </CardContent>
              </ScrollSurface>
            </Card>
          </TabsContent>

          <TabsContent value="permissions" class="mt-0">
            <div class="grid h-full min-h-0 gap-4 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('administration.label.templatePermissions') }}</CardTitle>
                  <CardDescription>{{ $t('administration.challengesBy.description.personChargePermissionsAdministrator') }}</CardDescription>
                </CardHeader>
                <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel>{{ $t('administration.label.responsiblePersonId') }}</FieldLabel>
                      <Input :model-value="template.ownerId" disabled class="font-mono text-sm" />
                    </Field>
                    <Field>
                      <FieldLabel for="managers">{{ $t('administration.label.administratorIdList') }}</FieldLabel>
                      <Textarea
                        id="managers"
                        v-model="managersText"
                        rows="5"
                        class="font-mono text-sm"
                        :placeholder="$t('administration.challengesBy.description.oneUserUuidLine')"
                        :disabled="isDeleted"
                      />
                      <FieldDescription>{{ $t('administration.challengesBy.validation.personChargeFormat') }}</FieldDescription>
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button :disabled="permissionsSaving" @click="savePermissions">
                        <Spinner v-if="permissionsSaving" data-icon="inline-start" /> {{ $t('administration.label.savePermissions') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
                </ScrollSurface>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('administration.label.transferPerson') }}</CardTitle>
                  <CardDescription>{{ $t('administration.challengesBy.description.transferTemplateOwnerAnother') }}</CardDescription>
                </CardHeader>
                <ScrollSurface axis="y" :reset-key="selectedSection" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
              <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="new-owner">{{ $t('administration.challengesBy.description.newPersonChargeUser') }}</FieldLabel>
                      <Input
                        id="new-owner"
                        v-model="newOwnerId"
                        class="font-mono text-sm"
                        :placeholder="$t('administration.label.userUuid')"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button variant="destructive" :disabled="!newOwnerId.trim()" @click="onClickTransferOpen(true)"> {{ $t('administration.label.transferPerson') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
                </ScrollSurface>
              </Card>
            </div>
          </TabsContent>
          </div>
        </Tabs>
      </template>
    </template>

    <AlertDialog :open="!!deletingAttachment" @update:open="onUpdateOpenDeletingAttachment">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.deleteAttachment') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.challengesBy.description.softDeleteAttachmentRestored', { file: deletingAttachment?.fileName ?? $t('common.label.symbol') }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button variant="destructive" :disabled="attachmentActionPending" @click="confirmDeleteAttachment">
            <Spinner v-if="attachmentActionPending" data-icon="inline-start" /> {{ $t('administration.label.confirmDeletion') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="randomBatchOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{{ $t('administration.label.teamRandomAttachments') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.challengesBy.description.completeOriginalFilenameParsed') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="random-download-name">{{ $t('administration.label.participantDownloadFilename') }}</FieldLabel>
            <Input id="random-download-name" v-model="randomDownloadFileName" :placeholder="$t('administration.label.challengeZip')" />
          </Field>
          <Field>
            <FieldLabel>{{ $t('administration.label.attachmentVariants') }}</FieldLabel>
            <FileInput :ref="setRandomUploadInputRef"  multiple class="hidden" @change="selectRandomFiles" />
            <Button variant="outline" :disabled="randomUploading" @click="randomUploadInput?.click()">
              <Upload data-icon="inline-start" /> {{ $t('administration.label.selectMultipleFiles') }}
            </Button>
            <FieldDescription>{{ $t('administration.challengesBy.validation.variantsOriginalFormat', { count: randomFiles.length }) }}</FieldDescription>
            <ScrollSurface as="div" v-if="randomFiles.length" class="max-h-56 overflow-auto rounded-md border p-3">
              <div v-for="file in randomFiles" :key="`${file.name}-${file.size}-${file.lastModified}`" class="flex justify-between gap-4 py-1 text-sm">
                <span class="truncate font-mono">{{ file.name }}</span>
                <span class="shrink-0 text-muted-foreground">{{ formatBytes(file.size) }}</span>
              </div>
            </ScrollSurface>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="randomUploading" @click="onClickRandomBatchOpen2(false)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="randomUploading || !randomDownloadFileName.trim() || randomFiles.length === 0" @click="uploadRandomBatch">
            <Spinner v-if="randomUploading" data-icon="inline-start" /> {{ $t('administration.label.uploadEntireBatch') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!deletingFlag" @update:open="onUpdateOpenDeletingFlag">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.deleteFlag') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.challengesBy.description.softDeleteFlagRestored', { flag: deletingFlag?.flag ?? $t('common.label.symbol') }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button variant="destructive" :disabled="flagActionPending" @click="confirmDeleteFlag">
            <Spinner v-if="flagActionPending" data-icon="inline-start" /> {{ $t('administration.label.confirmDeletion') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="transferOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.transferPerson') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.challengesBy.description.transferOwnershipUserLose', { title: template?.title ?? $t('common.label.symbol'), user: newOwnerId }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transferOwner">
            <Spinner v-if="transferring" data-icon="inline-start" /> {{ $t('common.label.confirmTransfer') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
