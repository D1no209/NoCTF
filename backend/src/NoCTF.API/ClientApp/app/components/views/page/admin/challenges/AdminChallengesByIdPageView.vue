<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminChallengesByIdPageViewState } from '~/features/routes/admin/challenges/useAdminChallengesByIdPage'

const viewProps = defineProps<{ state: AdminChallengesByIdPageViewState }>()
const { Paperclip, RotateCcw, Trash2, Upload, challengeId, canOrganize, template, loading, loadError, form, saving, saveErrors, deleting, restoring, isDeleted, titleInvalid, directionInvalid, definitionModel, definitionParseFailed, hasModeDefinition, usesRuntimeFlagInjection, runtimeDisabled, runtimeDefinitionDirty, CtfInteraction, showInteractionKind, setInteractionKind, changeMode, resetDefinitionToCurrentMode, save, removeTemplate, restoreTemplate, attachments, attachmentsLoading, attachmentsIncludeDeleted, attachmentDeliveryPolicy, uploading, uploadInput, randomBatchOpen, randomUploading, randomDownloadFileName, randomFiles, randomUploadInput, deletingAttachment, attachmentActionPending, requestAttachmentDeliveryPolicy, uploadAttachment, selectRandomFiles, uploadRandomBatch, confirmDeleteAttachment, restoreAttachment, formatBytes, flags, supportsRegularExpression, flagsLoading, flagsIncludeDeleted, flagCreateOpen, flagCreating, flagForm, deletingFlag, flagActionPending, staticFlags, systemFlags, openFlagCreate, createFlag, confirmDeleteFlag, restoreFlag, managersText, permissionsSaving, newOwnerId, transferOpen, transferring, savePermissions, transferOwner, AdminDateTime, AdminGameModeBadge, ChallengeTestRuntimePanel, DefinitionCheckerSection, DefinitionFlagInjectionSection, DefinitionFlagTemplateSection, DefinitionPatchSection, DefinitionRuntimeSection, setUploadInputRef, setRandomUploadInputRef, onBlurFormDirection, onClickRandomBatchOpen, onClickDeletingAttachment, onClickDeletingFlag, onClickTransferOpen, onUpdateOpenDeletingAttachment, onClickRandomBatchOpen2, onClickFlagCreateOpen, onUpdateOpenDeletingFlag } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex items-center gap-2 text-sm text-muted-foreground">
      <NuxtLink to="/admin/challenges" class="hover:underline">{{ $t('ui.challengeLibrary2') }}</NuxtLink>
      <span>/</span>
      <span>{{ template?.title ?? challengeId }}</span>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('ui.organizerOrAdministratorRightsAreRequiredToManageTheQuestion') }}</AlertDescription>
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
          <div class="flex items-center gap-3">
            <h1 class="text-2xl font-semibold">{{ template.title }}</h1>
            <component :is="AdminGameModeBadge" :mode="template.mode" />
            <Badge v-if="isDeleted" variant="destructive">{{ $t('ui.deleted') }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <Button v-if="isDeleted" variant="outline" :disabled="restoring" @click="restoreTemplate">
              <Spinner v-if="restoring" data-icon="inline-start" />
              <RotateCcw v-else data-icon="inline-start" /> {{ $t('ui.recoveryTemplate') }} </Button>
            <AlertDialog v-else>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">
                  <Trash2 data-icon="inline-start" /> {{ $t('ui.deleteTemplate') }} </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ $t('ui.deleteQuestionTemplate') }}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {{ $t('ui.softDeleteTemplateItCanBeRestoredLaterAndHistorical', { title: template.title ?? $t('ui.symbol') }) }}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" :disabled="deleting" @click="removeTemplate">
                    <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('ui.confirmDeletion') }} </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>

        <Alert v-if="saveErrors.length" variant="destructive">
          <AlertTitle>{{ $t('ui.unableToSaveTheChallengeTemplate') }}</AlertTitle>
          <AlertDescription class="flex flex-col gap-2">
            <span>{{ $t('ui.correctTheFollowingIssuesAndTryAgain') }}</span>
            <ul class="list-disc pl-5">
              <li v-for="message in saveErrors" :key="message">{{ message }}</li>
            </ul>
          </AlertDescription>
        </Alert>

        <Tabs
          default-value="basic"
          orientation="vertical"
          class="grid items-start gap-6 lg:grid-cols-[14rem_minmax(0,1fr)]"
        >
          <aside class="border-b pb-4 lg:sticky lg:top-20 lg:border-r lg:border-b-0 lg:pr-4 lg:pb-0">
            <TabsList class="grid h-auto w-full grid-cols-2 items-stretch gap-1 bg-transparent p-0 sm:grid-cols-3 lg:flex lg:flex-col">
              <TabsTrigger value="basic" class="w-full justify-start px-3 py-2">{{ $t('ui.basicInformation') }}</TabsTrigger>
              <TabsTrigger value="runtime" class="w-full justify-start px-3 py-2">{{ $t('ui.runtimeEnvironment') }}</TabsTrigger>
              <TabsTrigger value="definition" class="w-full justify-start px-3 py-2">{{ $t('ui.modeDefinition') }}</TabsTrigger>
              <TabsTrigger value="attachments" class="w-full justify-start px-3 py-2">
                {{ $t('ui.accessories') }}
                <Badge variant="secondary" class="ml-auto">{{ attachments.length }}</Badge>
              </TabsTrigger>
              <TabsTrigger value="flags" class="w-full justify-start px-3 py-2">
                {{ $t('ui.flags') }}
                <Badge variant="secondary" class="ml-auto">{{ flags.length }}</Badge>
              </TabsTrigger>
              <TabsTrigger value="permissions" class="w-full justify-start px-3 py-2">{{ $t('ui.permissions') }}</TabsTrigger>
            </TabsList>
          </aside>

          <div class="min-w-0">
          <TabsContent value="basic" class="mt-0">
            <Card>
              <CardContent class="pt-6">
                <UiForm validation="feature" @submit.prevent="save">
                  <FieldGroup>
                    <Field :data-invalid="titleInvalid || undefined">
                      <FieldLabel for="edit-title">{{ $t('ui.title') }}</FieldLabel>
                      <Input
                        id="edit-title"
                        v-model="form.title"
                        required
                        maxlength="160"
                        :aria-invalid="titleInvalid || undefined"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <div class="grid gap-4 sm:grid-cols-2">
                      <Field>
                        <FieldLabel for="edit-mode">{{ $t('ui.gameMode') }}</FieldLabel>
                        <Select :model-value="form.mode" :disabled="isDeleted" @update:model-value="changeMode">
                          <SelectTrigger id="edit-mode" class="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="Ctf">{{ $t('ui.ctf') }}</SelectItem>
                              <SelectItem value="Awd">{{ $t('ui.awd') }}</SelectItem>
                              <SelectItem value="Awdp">{{ $t('ui.awdp') }}</SelectItem>
                              <SelectItem value="Koh">{{ $t('ui.koh') }}</SelectItem>
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                        <FieldDescription>{{ $t('ui.theModeCannotBeModifiedWhileThereIsAnOngoing') }}</FieldDescription>
                      </Field>
                      <Field>
                        <FieldLabel for="edit-visibility">{{ $t('ui.visibility') }}</FieldLabel>
                        <Select v-model="form.visibility" :disabled="isDeleted">
                          <SelectTrigger id="edit-visibility" class="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="Private">{{ $t('ui.private') }}</SelectItem>
                              <SelectItem value="Shared">{{ $t('ui.share') }}</SelectItem>
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      </Field>
                    </div>
                    <Field :data-invalid="directionInvalid || undefined">
                      <FieldLabel for="edit-direction">{{ $t('ui.category') }}</FieldLabel>
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
                    <Field>
                      <FieldLabel for="edit-description">{{ $t('ui.question') }}</FieldLabel>
                      <Textarea id="edit-description" v-model="form.description" rows="8" :disabled="isDeleted" />
                    </Field>
                    <div class="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                      <span>{{ $t('ui.create') }} <component :is="AdminDateTime" :value="template.createdAt" /></span>
                      <span>{{ $t('ui.update') }} <component :is="AdminDateTime" :value="template.updatedAt" /></span>
                      <span v-if="template.deletedAt">{{ $t('ui.delete') }} <component :is="AdminDateTime" :value="template.deletedAt" /></span>
                    </div>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button type="submit" :disabled="saving">
                        <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.saveChanges') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="runtime" class="mt-0">
            <Card>
              <CardContent class="pt-6">
                <Alert v-if="definitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('ui.theExistingDefinitionJsonCannotBeParsedAndMayBe2') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('ui.resetToCurrentModeDefinition') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="definitionModel">
                  <div v-if="!isDeleted" class="flex flex-wrap items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('ui.resetToCurrentModeDefinition') }}
                    </Button>
                    <Button data-testid="runtime-definition-save" type="button" size="sm" :disabled="saving" @click="save">
                      <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.saveChanges') }}
                    </Button>
                  </div>
                  <component :is="DefinitionRuntimeSection" :model="definitionModel" :mode="form.mode" :disabled="isDeleted" />
                  <FieldDescription>{{ $t('ui.runtimeDefinitionChangesTakeEffectForInstancesStartedInThe') }}</FieldDescription>
                  <component :is="ChallengeTestRuntimePanel"
                    v-if="definitionModel.runtime && !isDeleted"
                    :challenge-id="challengeId"
                    :definition-dirty="runtimeDefinitionDirty"
                  />
                </FieldGroup>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="definition" class="mt-0">
            <Card>
              <CardContent class="pt-6">
                <Alert v-if="definitionParseFailed" variant="destructive">
                  <AlertDescription class="flex flex-col items-start gap-3">
                    <span>{{ $t('ui.theExistingDefinitionJsonCannotBeParsedAndMayBe3') }}</span>
                    <Button v-if="!isDeleted" type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('ui.resetToCurrentModeDefinition') }}
                    </Button>
                  </AlertDescription>
                </Alert>
                <FieldGroup v-else-if="definitionModel">
                  <div v-if="!isDeleted" class="flex flex-wrap items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" @click="resetDefinitionToCurrentMode">
                      <RotateCcw data-icon="inline-start" /> {{ $t('ui.resetToCurrentModeDefinition') }}
                    </Button>
                    <Button data-testid="mode-definition-save" type="button" size="sm" :disabled="saving" @click="save">
                      <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.saveChanges') }}
                    </Button>
                  </div>
                  <Alert v-if="hasModeDefinition && runtimeDisabled">
                    <AlertDescription>{{ $t('ui.theFollowingConfigurationsWillNotTakeEffectWhileTheRuntime') }}</AlertDescription>
                  </Alert>
                  <Field v-if="showInteractionKind">
                    <FieldLabel for="challenge-ctf-interaction-kind">{{ $t('ui.completionMethod') }}</FieldLabel>
                    <Select
                      :model-value="definitionModel.interactionKind === CtfInteraction.PatchVerification ? 'PatchVerification' : 'FlagSubmission'"
                      :disabled="isDeleted"
                      @update:model-value="setInteractionKind"
                    >
                      <SelectTrigger id="challenge-ctf-interaction-kind" class="w-full"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="FlagSubmission">{{ $t('ui.flagSubmission') }}</SelectItem>
                          <SelectItem value="PatchVerification">{{ $t('ui.patchVerification') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                    <FieldDescription>{{ $t('ui.ctfCompletionMethodDescription') }}</FieldDescription>
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
                      <EmptyTitle>{{ $t('ui.thisModeHasNoAdditionalModeDefinition') }}</EmptyTitle>
                    </EmptyHeader>
                  </Empty>
                  <FieldDescription v-else> {{ $t('ui.definitionModificationsWillTakeEffectOnInstancesThatAreStarted') }} </FieldDescription>
                </FieldGroup>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="attachments" class="mt-0">
            <Card>
              <CardHeader class="gap-4">
                <div class="flex flex-wrap items-center justify-between gap-4">
                  <div>
                    <CardTitle>{{ $t('ui.attachmentDelivery') }}</CardTitle>
                    <CardDescription>
                      {{ attachmentDeliveryPolicy === 'RandomOnePerTeam'
                        ? $t('ui.eachTeamReceivesOneStableRandomAttachmentVariantOnIts')
                        : $t('ui.participantsCanViewAndDownloadEveryStandardAttachment') }}
                    </CardDescription>
                  </div>
                  <Badge variant="outline">
                    {{ attachmentDeliveryPolicy === 'RandomOnePerTeam' ? $t('ui.oneRandomVariantPerTeam') : $t('ui.allAttachments') }}
                  </Badge>
                </div>
                <div class="flex flex-wrap items-end justify-between gap-3">
                  <div class="flex items-center gap-2">
                    <Switch id="attachments-include-deleted" v-model="attachmentsIncludeDeleted" />
                    <Label for="attachments-include-deleted">{{ $t('ui.showDeleted') }}</Label>
                  </div>
                  <div class="flex flex-wrap items-end gap-2">
                    <div class="grid min-w-48 gap-1.5">
                      <Label for="attachment-delivery-policy">{{ $t('ui.deliveryMode') }}</Label>
                      <Select
                        :model-value="attachmentDeliveryPolicy"
                        :disabled="isDeleted || uploading || randomUploading"
                        @update:model-value="requestAttachmentDeliveryPolicy"
                      >
                        <SelectTrigger id="attachment-delivery-policy" class="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="All">{{ $t('ui.allAttachments') }}</SelectItem>
                          <SelectItem value="RandomOnePerTeam">{{ $t('ui.oneRandomVariantPerTeam') }}</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <template v-if="attachmentDeliveryPolicy === 'All'">
                      <FileInput :ref="setUploadInputRef"  multiple class="hidden" @change="uploadAttachment" />
                      <Button :disabled="uploading || isDeleted" @click="uploadInput?.click()">
                        <Spinner v-if="uploading" data-icon="inline-start" />
                        <Upload v-else data-icon="inline-start" /> {{ $t('ui.uploadStandardAttachments') }}
                      </Button>
                      <Button
                        variant="outline"
                        :disabled="isDeleted || attachments.some(item => !item.deletedAt)"
                        @click="onClickRandomBatchOpen(true)"
                      >
                        {{ $t('ui.createPerTeamRandomAttachments') }}
                      </Button>
                    </template>
                    <Button v-else :disabled="isDeleted" @click="onClickRandomBatchOpen(true)">
                      {{ $t('ui.uploadRandomAttachmentBatch') }}
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                <div v-if="attachmentsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="attachments.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('ui.noAttachmentsYet') }}</EmptyTitle>
                    <EmptyDescription>{{ $t('ui.uploadTheAttachmentFilesRequiredForTheQuestion') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <Table v-else>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{{ $t('ui.fileName') }}</TableHead>
                      <TableHead v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'">{{ $t('ui.exactFlag') }}</TableHead>
                      <TableHead v-else>{{ $t('ui.type') }}</TableHead>
                      <TableHead>{{ $t('ui.hash') }}</TableHead>
                      <TableHead>{{ $t('ui.size') }}</TableHead>
                      <TableHead>{{ $t('ui.uploadTime') }}</TableHead>
                      <TableHead>{{ $t('ui.status') }}</TableHead>
                      <TableHead class="text-right">{{ $t('ui.actions') }}</TableHead>
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
                        {{ attachment.exactFlag ?? $t('ui.symbol') }}
                      </TableCell>
                      <TableCell v-else class="text-muted-foreground">{{ attachment.contentType }}</TableCell>
                      <Hint :content="attachment.sha256" ><TableCell tabindex="0"
                        class="font-mono text-xs tabular-nums text-muted-foreground"

                      >
                        {{ attachment.sha256?.slice(0, 8) ?? $t('ui.symbol') }}
                      </TableCell></Hint>
                      <TableCell>{{ formatBytes(attachment.byteLength) }}</TableCell>
                      <TableCell>
                        <component :is="AdminDateTime" :value="attachment.createdAt" />
                      </TableCell>
                      <TableCell>
                        <Badge v-if="attachment.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('ui.normal') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <div class="flex justify-end gap-2">
                          <template v-if="attachment.deletedAt">
                            <Button
                              size="sm"
                              variant="outline"
                              :disabled="attachmentActionPending"
                              @click="restoreAttachment(attachment)"
                            > {{ $t('ui.restore2') }} </Button>
                          </template>
                          <template v-else>
                            <Button size="sm" variant="destructive" @click="onClickDeletingAttachment(attachment)"> {{ $t('ui.delete') }} </Button>
                          </template>
                        </div>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="flags" class="mt-0">
            <Card>
              <CardHeader class="flex flex-row items-center justify-between gap-4">
                <div class="flex items-center gap-2">
                  <Switch id="flags-include-deleted" v-model="flagsIncludeDeleted" />
                  <Label for="flags-include-deleted">{{ $t('ui.showDeleted') }}</Label>
                </div>
                <Button v-if="!usesRuntimeFlagInjection" :disabled="isDeleted" @click="openFlagCreate">
                  {{ $t('ui.addFlag2') }}
                </Button>
              </CardHeader>
              <CardContent>
                <Alert v-if="usesRuntimeFlagInjection" class="mb-4">
                  <AlertDescription>
                    {{ $t('ui.thisChallengeUsesRuntimeManagedDynamicFlagsThePlatformGenerates') }}
                  </AlertDescription>
                </Alert>
                <div v-if="flagsLoading" class="flex flex-col gap-2">
                  <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
                </div>
                <Empty v-else-if="staticFlags.length === 0 && systemFlags.length === 0">
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('ui.noFlagYet') }}</EmptyTitle>
                    <EmptyDescription v-if="!usesRuntimeFlagInjection">{{ $t('ui.addTemplateLevelStaticFlag') }}</EmptyDescription>
                    <EmptyDescription v-else>{{ $t('ui.aDynamicFlagWillBeGeneratedAutomaticallyWhenATeam') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
                <div v-else class="space-y-6">
                  <section v-if="staticFlags.length > 0" class="space-y-2">
                    <h3 class="text-sm font-semibold">{{ $t('ui.staticFlags') }}</h3>
                    <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{{ $t('ui.flag4') }}</TableHead>
                      <TableHead>{{ $t('ui.status') }}</TableHead>
                      <TableHead class="text-right">{{ $t('ui.actions') }}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="flag in staticFlags" :key="flag.id">
                      <Hint :content="flag.flag" ><TableCell tabindex="0" class="max-w-md" >
                        <div class="flex min-w-0 items-center gap-2">
                          <Badge variant="outline">
                            {{ flag.matchKind === 'RegularExpression' ? $t('ui.regularExpression') : $t('ui.exactMatch') }}
                          </Badge>
                          <span class="truncate font-mono text-sm">{{ flag.flag }}</span>
                        </div>
                      </TableCell></Hint>
                      <TableCell>
                        <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
                        <Badge v-else variant="secondary">{{ $t('ui.normal') }}</Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <Button
                          v-if="flag.deletedAt"
                          size="sm"
                          variant="outline"
                          :disabled="flagActionPending"
                          @click="restoreFlag(flag)"
                        > {{ $t('ui.restore2') }} </Button>
                        <Button v-else size="sm" variant="destructive" @click="onClickDeletingFlag(flag)"> {{ $t('ui.delete') }} </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                    </Table>
                  </section>

                  <section v-if="systemFlags.length > 0" class="space-y-2">
                    <div>
                      <h3 class="text-sm font-semibold">{{ $t('ui.systemGeneratedDynamicFlags') }}</h3>
                      <p class="text-sm text-muted-foreground">{{ $t('ui.automaticallyLinkedToAnAttachmentTeamRoundOrRuntimeBy') }}</p>
                    </div>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>{{ $t('ui.flag4') }}</TableHead>
                          <TableHead>{{ $t('ui.team') }}</TableHead>
                          <TableHead>{{ $t('ui.internalLink') }}</TableHead>
                          <TableHead>{{ $t('ui.validityPeriod') }}</TableHead>
                          <TableHead>{{ $t('ui.status') }}</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        <TableRow v-for="flag in systemFlags" :key="flag.id">
                          <Hint :content="flag.flag" ><TableCell tabindex="0" class="max-w-md truncate font-mono text-sm" >{{ flag.flag }}</TableCell></Hint>
                          <TableCell class="font-mono text-xs">{{ flag.teamId ?? $t('ui.symbol') }}</TableCell>
                          <TableCell class="font-mono text-xs">
                            {{ flag.specificationKind ?? $t('ui.symbol') }}<span v-if="flag.specificationId"> · {{ flag.specificationId }}</span>
                          </TableCell>
                          <TableCell class="text-sm text-muted-foreground">
                            <template v-if="flag.validStart || flag.validUntil">
                              <component :is="AdminDateTime" :value="flag.validStart" /> {{ $t('ui.to') }} <component :is="AdminDateTime" :value="flag.validUntil" />
                            </template>
                            <span v-else>{{ $t('ui.effectiveForALongTime') }}</span>
                          </TableCell>
                          <TableCell>
                            <Badge v-if="flag.deletedAt" variant="destructive">{{ $t('ui.expired3') }}</Badge>
                            <Badge v-else variant="secondary">{{ $t('ui.injected') }}</Badge>
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </section>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="permissions" class="mt-0">
            <div class="grid gap-6 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('ui.templatePermissions') }}</CardTitle>
                  <CardDescription>{{ $t('ui.thePersonInChargeHasAllPermissionsTheAdministratorCan') }}</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel>{{ $t('ui.responsiblePersonId') }}</FieldLabel>
                      <Input :model-value="template.ownerId" disabled class="font-mono text-sm" />
                    </Field>
                    <Field>
                      <FieldLabel for="managers">{{ $t('ui.administratorIdList') }}</FieldLabel>
                      <Textarea
                        id="managers"
                        v-model="managersText"
                        rows="5"
                        class="font-mono text-sm"
                        :placeholder="$t('ui.oneUserUuidPerLineOrSeparatedByCommas')"
                        :disabled="isDeleted"
                      />
                      <FieldDescription>{{ $t('ui.thePersonInChargeCannotBeIncludedInTheAdministrator') }}</FieldDescription>
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button :disabled="permissionsSaving" @click="savePermissions">
                        <Spinner v-if="permissionsSaving" data-icon="inline-start" /> {{ $t('ui.savePermissions') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>{{ $t('ui.transferPerson') }}</CardTitle>
                  <CardDescription>{{ $t('ui.transferTheTemplateOwnerToAnotherOrganizerOrAdministratorEffective') }}</CardDescription>
                </CardHeader>
                <CardContent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="new-owner">{{ $t('ui.newPersonInChargeUserId') }}</FieldLabel>
                      <Input
                        id="new-owner"
                        v-model="newOwnerId"
                        class="font-mono text-sm"
                        :placeholder="$t('ui.userUuid')"
                        :disabled="isDeleted"
                      />
                    </Field>
                    <Field v-if="!isDeleted" orientation="horizontal">
                      <Button variant="destructive" :disabled="!newOwnerId.trim()" @click="onClickTransferOpen(true)"> {{ $t('ui.transferPerson') }} </Button>
                    </Field>
                  </FieldGroup>
                </CardContent>
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
          <AlertDialogTitle>{{ $t('ui.deleteAttachment') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.softDeleteAttachmentItCanBeRestoredLater', { file: deletingAttachment?.fileName ?? $t('ui.symbol') }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button variant="destructive" :disabled="attachmentActionPending" @click="confirmDeleteAttachment">
            <Spinner v-if="attachmentActionPending" data-icon="inline-start" /> {{ $t('ui.confirmDeletion') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="randomBatchOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{{ $t('ui.perTeamRandomAttachments') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.eachCompleteOriginalFilenameIsParsedAsAnExactFlag') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="random-download-name">{{ $t('ui.participantDownloadFilename') }}</FieldLabel>
            <Input id="random-download-name" v-model="randomDownloadFileName" :placeholder="$t('ui.challengeZip')" />
          </Field>
          <Field>
            <FieldLabel>{{ $t('ui.attachmentVariants') }}</FieldLabel>
            <FileInput :ref="setRandomUploadInputRef"  multiple class="hidden" @change="selectRandomFiles" />
            <Button variant="outline" :disabled="randomUploading" @click="randomUploadInput?.click()">
              <Upload data-icon="inline-start" /> {{ $t('ui.selectMultipleFiles') }}
            </Button>
            <FieldDescription>{{ $t('ui.selectedVariantsOriginalFilenamesMustBeUniqueWithinTheBatch', { count: randomFiles.length }) }}</FieldDescription>
            <ScrollSurface as="div" v-if="randomFiles.length" class="max-h-56 overflow-auto rounded-md border p-3">
              <div v-for="file in randomFiles" :key="`${file.name}-${file.size}-${file.lastModified}`" class="flex justify-between gap-4 py-1 text-sm">
                <span class="truncate font-mono">{{ file.name }}</span>
                <span class="shrink-0 text-muted-foreground">{{ formatBytes(file.size) }}</span>
              </div>
            </ScrollSurface>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="randomUploading" @click="onClickRandomBatchOpen2(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="randomUploading || !randomDownloadFileName.trim() || randomFiles.length === 0" @click="uploadRandomBatch">
            <Spinner v-if="randomUploading" data-icon="inline-start" /> {{ $t('ui.uploadEntireBatch') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="flagCreateOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.addFlag2') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.templateLevelStaticFlagWhichCanBeReferencedWhenInstantiated') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="flag-match-kind">{{ $t('ui.matchType') }}</FieldLabel>
            <Select v-model="flagForm.matchKind">
              <SelectTrigger id="flag-match-kind" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Exact">{{ $t('ui.exactMatch') }}</SelectItem>
                <SelectItem v-if="supportsRegularExpression" value="RegularExpression">
                  {{ $t('ui.regularExpression') }}
                </SelectItem>
              </SelectContent>
            </Select>
            <FieldDescription>
              {{ $t('ui.theRegularExpressionMatchesTheEntireFlagAndIsCase') }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="flag-value">
              {{ flagForm.matchKind === 'RegularExpression' ? $t('ui.regularExpression2') : $t('ui.flagContent') }}
            </FieldLabel>
            <Input
              id="flag-value"
              v-model="flagForm.flag"
              required
              class="font-mono text-sm"
              :placeholder="flagForm.matchKind === 'RegularExpression' ? $t('ui.flag09aF36') : $t('ui.flag3')"
            />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickFlagCreateOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="flagCreating || !flagForm.flag.trim()" @click="createFlag">
            <Spinner v-if="flagCreating" data-icon="inline-start" /> {{ $t('ui.add') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!deletingFlag" @update:open="onUpdateOpenDeletingFlag">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.deleteFlag') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.softDeleteThisFlagItCanBeRestoredLater', { flag: deletingFlag?.flag ?? $t('ui.symbol') }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button variant="destructive" :disabled="flagActionPending" @click="confirmDeleteFlag">
            <Spinner v-if="flagActionPending" data-icon="inline-start" /> {{ $t('ui.confirmDeletion') }} </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="transferOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.transferPerson') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.transferOwnershipOfToUserYouWillLoseOwnershipImmediately', { title: template?.title ?? $t('ui.symbol'), user: newOwnerId }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transferOwner">
            <Spinner v-if="transferring" data-icon="inline-start" /> {{ $t('ui.confirmTransfer') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
