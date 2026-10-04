<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeTemplateCreateDialogViewState } from '~/features/admin/useChallengeTemplateCreateDialog'

const viewProps = defineProps<{ state: ChallengeTemplateCreateDialogViewState }>()
const { open, title, mode, visibility, direction, description, definition, saveErrors, pending, titleInvalid, directionInvalid, directionOptions, changeMode, setOpen, submit, DefinitionEditor } = toRefs(viewProps.state)
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col gap-0 overflow-hidden p-0 sm:max-w-4xl">
      <DialogHeader class="shrink-0 border-b px-6 py-5 pr-14">
        <DialogTitle>{{ $t('administration.challengeTemplate.label.createNewQuestionTemplate') }}</DialogTitle>
        <DialogDescription>{{ $t('administration.challengeTemplate.description.optionalConfigureLaterCreation') }}</DialogDescription>
      </DialogHeader>

      <UiForm validation="feature" class="flex min-h-0 flex-1 flex-col" @submit.prevent="submit">
        <Alert v-if="saveErrors.length" variant="destructive" class="mx-6 mt-5 shrink-0">
          <AlertTitle>{{ $t('administration.challengesBy.description.unableSaveChallengeTemplate') }}</AlertTitle>
          <AlertDescription class="flex flex-col gap-2">
            <span>{{ $t('administration.challengeTemplate.description.correctFollowingIssuesTry') }}</span>
            <ul class="list-disc pl-5">
              <li v-for="(message, index) in saveErrors" :key="index ?? undefined">{{ message }}</li>
            </ul>
          </AlertDescription>
        </Alert>

        <Tabs default-value="basic" class="flex min-h-0 flex-1 flex-col px-6 pt-5">
          <TabsList class="shrink-0 self-start">
            <TabsTrigger value="basic">{{ $t('administration.label.basicInformation') }}</TabsTrigger>
            <TabsTrigger value="definition">{{ $t('administration.label.questionDefinition') }}</TabsTrigger>
          </TabsList>
          <ScrollSurface axis="y" class="mt-4 -mx-3 min-h-0 flex-1 overscroll-contain px-3" :aria-label="$t('administration.challengeTemplate.label.createNewQuestionTemplate')">
            <div class="px-1">
            <TabsContent value="basic" class="mt-0 pb-5">
              <FieldGroup>
                <Field :data-invalid="titleInvalid || undefined">
                  <FieldLabel for="create-template-title">{{ $t('common.label.title') }}</FieldLabel>
                  <Input id="create-template-title" v-model="title" required maxlength="160" :aria-invalid="titleInvalid || undefined" :placeholder="$t('administration.challengeTemplate.description.exampleGettingStartedWeb')" />
                </Field>
                <div class="grid gap-4 sm:grid-cols-2">
                  <Field>
                    <FieldLabel for="create-template-mode">{{ $t('common.label.gameMode') }}</FieldLabel>
                    <Select :model-value="mode" @update:model-value="changeMode">
                      <SelectTrigger id="create-template-mode" class="w-full"><SelectValue :placeholder="$t('common.label.selectMode')" /></SelectTrigger>
                      <SelectContent><SelectGroup>
                        <SelectItem value="Ctf">{{ $t('common.label.ctf') }}</SelectItem>
                        <SelectItem value="Awd">{{ $t('common.label.awd') }}</SelectItem>
                        <SelectItem value="Awdp">{{ $t('common.label.awdp.createDialogView') }}</SelectItem>
                        <SelectItem value="Koh">{{ $t('common.label.koh') }}</SelectItem>
                      </SelectGroup></SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="create-template-visibility">{{ $t('administration.label.visibility') }}</FieldLabel>
                    <Select v-model="visibility">
                      <SelectTrigger id="create-template-visibility" class="w-full"><SelectValue :placeholder="$t('administration.label.selectVisibility')" /></SelectTrigger>
                      <SelectContent><SelectGroup>
                        <SelectItem value="Private">{{ $t('administration.challengeTemplate.description.privateAvailablePersonCharge') }}</SelectItem>
                        <SelectItem value="Shared">{{ $t('administration.challengeTemplate.label.shareAvailableOrganizers') }}</SelectItem>
                      </SelectGroup></SelectContent>
                    </Select>
                  </Field>
                </div>
                <Field :data-invalid="directionInvalid || undefined">
                  <FieldLabel for="create-template-direction">{{ $t('administration.label.category') }}</FieldLabel>
                  <Select v-model="direction">
                    <SelectTrigger id="create-template-direction" class="w-full" :aria-invalid="directionInvalid || undefined"><SelectValue /></SelectTrigger>
                    <SelectContent><SelectGroup>
                      <SelectItem v-for="option in directionOptions" :key="option ?? undefined" :value="option">{{ directionLabel(option) }}</SelectItem>
                    </SelectGroup></SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="create-template-description">{{ $t('administration.label.question') }}</FieldLabel>
                  <Textarea id="create-template-description" v-model="description" rows="6" :placeholder="$t('administration.label.titleDescriptionSupportsMarkdown')" />
                </Field>
              </FieldGroup>
            </TabsContent>
            <TabsContent value="definition" class="mt-0 pb-5">
              <component :is="DefinitionEditor" v-model="definition" :mode="mode" />
            </TabsContent>
            </div>
          </ScrollSurface>
        </Tabs>

        <DialogFooter class="shrink-0 border-t px-6 py-4">
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button type="submit" :disabled="pending">
            <Spinner v-if="pending" data-icon="inline-start" />{{ $t('administration.label.createTemplate') }}
          </Button>
        </DialogFooter>
      </UiForm>
    </DialogContent>
  </Dialog>
</template>
